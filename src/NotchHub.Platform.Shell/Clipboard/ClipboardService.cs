using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Platform.Shell.Imaging;
using NotchHub.Platform.Shell.Internal;
using NotchHub.Platform.Shell.Internal.Interop;
using WpfClipboard = System.Windows.Clipboard;

namespace NotchHub.Platform.Shell.Clipboard;

/// <summary>
/// Clipboard history. Listens with AddClipboardFormatListener on the shell STA thread, reads with retries,
/// skips sensitive content (password managers) and ignored apps, stores images as PNG + thumbnail under
/// <c>clipboard/images</c> and persists the index to <c>clipboard/history.json</c> (debounced).
/// Clipboard contents are never logged.
/// </summary>
public sealed class ClipboardService(ISettingsService settings) : IClipboardService
{
    private const int MaxTextLength = 1_000_000;
    private const long MaxImagePixels = 40_000_000;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1.5);

    private readonly ISettingsService _settings = settings;
    private readonly object _gate = new();
    private readonly List<ClipItem> _items = [];
    private ClipItem[] _snapshot = [];
    private StaThread.Lease? _lease;
    private IDisposable? _hook;
    private DispatcherTimer? _readTimer;
    private Timer? _saveTimer;
    private uint _ignoreSequence;
    private volatile bool _listening;
    private bool _started;
    private bool _disposed;
    private uint _fmtExclude, _fmtViewerIgnore, _fmtHistory, _fmtPng;

    /// <summary>History, newest first.</summary>
    public IReadOnlyList<ClipItem> Items => Volatile.Read(ref _snapshot);

    /// <summary>True while the clipboard listener is registered.</summary>
    public bool IsMonitoring => _listening;

    /// <summary>History or monitoring state changed (any thread).</summary>
    public event EventHandler? Changed;

    private static string ClipDir => AppPaths.Dir("clipboard");
    private static string HistoryPath => Path.Combine(ClipDir, "history.json");
    private static string ImageDir
    {
        get
        {
            var dir = Path.Combine(ClipDir, "images");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private ClipboardSettings Options => _settings.Current.Clipboard;

    /// <summary>Loads the history and starts listening (when enabled in settings).</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(LoadHistory, cancellationToken).ConfigureAwait(false);
        StaThread.Lease lease;
        try
        {
            lease = await StaThread.AcquireAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Clipboard: STA thread unavailable, history is read-only", ex);
            return;
        }
        lock (_gate)
        {
            if (_disposed) { lease.Dispose(); return; }
            _lease = lease;
            _started = true;
        }

        var sta = lease.Thread;
        await sta.InvokeAsync(() =>
        {
            _fmtExclude = Win32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            _fmtViewerIgnore = Win32.RegisterClipboardFormat("Clipboard Viewer Ignore");
            _fmtHistory = Win32.RegisterClipboardFormat("CanIncludeInClipboardHistory");
            _fmtPng = Win32.RegisterClipboardFormat("PNG");
            _readTimer = new DispatcherTimer(DispatcherPriority.Background, sta.Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
            _readTimer.Tick += (_, _) =>
            {
                _readTimer.Stop();
                CaptureAsync().Forget("Clipboard capture");
            };
        }).ConfigureAwait(false);
        _hook = sta.AddMessageHandler(OnMessage);
        _settings.Changed += OnSettingsChanged;
        await ApplySettingsAsync().ConfigureAwait(false);
    }

    // ───────────────────────────── listener ─────────────────────────────

    private void OnSettingsChanged(object? sender, EventArgs e) => ApplySettingsAsync().Forget("Clipboard settings");

    private async Task ApplySettingsAsync()
    {
        var sta = _lease?.Thread;
        if (sta is null || !sta.IsAlive) return;
        var enabled = Options.Enabled;
        var changed = await sta.InvokeAsync(() =>
        {
            if (enabled == _listening || _disposed) return false;
            if (enabled)
            {
                if (!Win32.AddClipboardFormatListener(sta.MessageWindow))
                {
                    Log.Warn($"Clipboard: AddClipboardFormatListener failed ({Win32.LastError()})");
                    return false;
                }
                _ignoreSequence = Win32.GetClipboardSequenceNumber(); // do not import what was there before start
            }
            else
            {
                Win32.RemoveClipboardFormatListener(sta.MessageWindow);
                _readTimer?.Stop();
            }
            _listening = enabled;
            return true;
        }).ConfigureAwait(false);

        bool pruned;
        lock (_gate) pruned = PruneLocked();
        if (pruned) Commit();
        else if (changed) RaiseChanged();
        if (changed) Log.Info($"Clipboard: monitoring {(enabled ? "on" : "off")}");
    }

    private bool OnMessage(nint hwnd, int msg, nint wParam, nint lParam)
    {
        if (msg != Win32.WM_CLIPBOARDUPDATE || !_listening) return false;
        // Apps often write several formats in bursts: read once things settle.
        _readTimer?.Stop();
        _readTimer?.Start();
        return true;
    }

    /// <summary>Runs on the STA thread.</summary>
    private async Task CaptureAsync()
    {
        if (!_listening || _disposed) return;
        var sequence = Win32.GetClipboardSequenceNumber();
        if (sequence == _ignoreSequence) return;
        var options = Options;

        if (options.IgnoreSensitive && (Win32.IsClipboardFormatAvailable(_fmtExclude) || Win32.IsClipboardFormatAvailable(_fmtViewerIgnore)))
        {
            return;
        }

        var owner = Win32.GetClipboardOwner();
        if (owner != 0 && Win32.GetWindowProcessId(owner) == (uint)ProcessInfo.CurrentProcessId) return;
        var identity = ProcessInfo.DescribeWindow(owner != 0 ? owner : Win32.GetForegroundWindow());
        if (identity is not null && ProcessInfo.MatchesExe(identity.ExeName, options.IgnoredApps)) return;
        var sourceApp = identity?.FriendlyName ?? AppServices.Foreground?.LastExternalAppName;

        var data = await ReadDataObjectAsync().ConfigureAwait(true);
        if (data is null) return;

        string[]? files = null;
        byte[]? png = null;
        BgraImage? bitmap = null;
        string? text = null;
        try
        {
            if (options.IgnoreSensitive && Win32.IsClipboardFormatAvailable(_fmtHistory)
                && data.GetData("CanIncludeInClipboardHistory") is MemoryStream { Length: >= 4 } flag)
            {
                var buffer = new byte[4];
                flag.ReadExactly(buffer);
                if (BitConverter.ToInt32(buffer) == 0) return;
            }

            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                files = (data.GetData(DataFormats.FileDrop) as string[])?.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
                if (files is { Length: 0 }) files = null;
            }

            var hasText = data.GetDataPresent(DataFormats.UnicodeText);
            var officeText = hasText && (data.GetDataPresent(DataFormats.Rtf) || data.GetDataPresent("XML Spreadsheet")
                                         || data.GetDataPresent(DataFormats.CommaSeparatedValue));
            if (files is null && options.SaveImages && !officeText)
            {
                if (Win32.IsClipboardFormatAvailable(_fmtPng) && data.GetData("PNG") is MemoryStream pngStream)
                {
                    png = pngStream.ToArray();
                }
                else if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource source)
                {
                    bitmap = ToBgra(source);
                }
            }

            if (files is null && png is null && bitmap is null && hasText)
            {
                text = data.GetData(DataFormats.UnicodeText) as string;
                if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength) return;
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException or InvalidOperationException)
        {
            Log.Warn($"Clipboard: reading the data failed ({ex.GetType().Name})");
            return;
        }

        var now = DateTimeOffset.Now;
        if (files is not null)
        {
            AddOrPromote(new ClipItem
            {
                Id = Guid.NewGuid(), Kind = ClipKind.Files, Files = files, Text = string.Join('\n', files),
                CopiedAt = now, SourceApp = sourceApp, Origin = ClipOrigin.Local,
            });
        }
        else if (png is not null || bitmap is not null)
        {
            var stored = await ImagingThread.RunAsync(() => StoreImage(png, bitmap)).ConfigureAwait(false);
            if (stored is null) return;
            AddOrPromote(new ClipItem
            {
                Id = Guid.NewGuid(), Kind = ClipKind.Image, ImagePath = stored.Value.Full, ThumbnailPath = stored.Value.Thumb,
                ImageWidth = stored.Value.Width, ImageHeight = stored.Value.Height,
                CopiedAt = now, SourceApp = sourceApp, Origin = ClipOrigin.Local,
            });
        }
        else if (text is not null)
        {
            AddOrPromote(new ClipItem
            {
                Id = Guid.NewGuid(), Kind = TextClassifier.Classify(text), Text = text,
                CopiedAt = now, SourceApp = sourceApp, Origin = ClipOrigin.Local,
            });
        }
    }

    private static async Task<IDataObject?> ReadDataObjectAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                return WpfClipboard.GetDataObject();
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                await Task.Delay(30 * (attempt + 1)).ConfigureAwait(true); // clipboard held open by another app
            }
        }
        Log.Warn("Clipboard: could not open the clipboard");
        return null;
    }

    private static BgraImage? ToBgra(BitmapSource source)
    {
        if ((long)source.PixelWidth * source.PixelHeight is 0 or > MaxImagePixels) return null;
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        var image = new BgraImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
        image.FixAllTransparent(); // 32-bit DIBs often carry alpha = 0
        return image;
    }

    /// <summary>Runs on the imaging thread: hashes, saves the PNG (once per content) and a ≤320 px thumbnail.</summary>
    private static (string Full, string Thumb, int Width, int Height)? StoreImage(byte[]? png, BgraImage? image)
    {
        try
        {
            if (image is null && png is not null)
            {
                using var stream = new MemoryStream(png, writable: false);
                var decoded = new BitmapImage();
                decoded.BeginInit();
                decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                decoded.StreamSource = stream;
                decoded.EndInit();
                decoded.Freeze();
                image = ToBgra(decoded);
            }
            if (image is null) return null;

            Span<byte> header = stackalloc byte[8];
            BitConverter.TryWriteBytes(header, image.Width);
            BitConverter.TryWriteBytes(header[4..], image.Height);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sha.AppendData(header);
            sha.AppendData(image.Pixels);
            var hash = Convert.ToHexString(sha.GetHashAndReset(), 0, 10).ToLowerInvariant();

            var dir = ImageDir;
            var full = Path.Combine(dir, hash + ".png");
            var thumb = Path.Combine(dir, hash + "_thumb.png");
            if (!File.Exists(full))
            {
                if (png is not null) WriteAtomic(full, png);
                else WritePng(full, image);
            }
            if (!File.Exists(thumb))
            {
                var (tw, th) = PixelScaler.FitWithin(image.Width, image.Height, 320);
                WritePng(thumb, tw == image.Width && th == image.Height ? image : PixelScaler.Downscale(image, tw, th));
            }
            return (full, thumb, image.Width, image.Height);
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard: storing an image failed", ex);
            return null;
        }
    }

    private static void WritePng(string path, BgraImage image)
    {
        var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        WriteAtomic(path, ms.ToArray());
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    // ───────────────────────────── history ─────────────────────────────

    private void LoadHistory()
    {
        var loaded = JsonStore.Load(HistoryPath, () => new List<ClipItem>());
        lock (_gate)
        {
            _items.Clear();
            foreach (var item in loaded.OrderByDescending(i => i.CopiedAt))
            {
                if (item.Kind == ClipKind.Image && (item.ImagePath is null || !File.Exists(item.ImagePath))) continue;
                _items.Add(item);
            }
            PruneLocked();
            Volatile.Write(ref _snapshot, [.. _items]);
        }
    }

    private static bool SameContent(ClipItem a, ClipItem b) => a.Kind == b.Kind && a.Kind switch
    {
        ClipKind.Image => string.Equals(a.ImagePath, b.ImagePath, StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(a.Text, b.Text, StringComparison.Ordinal),
    };

    private void AddOrPromote(ClipItem item)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(i => SameContent(i, item));
            if (index >= 0)
            {
                var existing = _items[index];
                _items.RemoveAt(index);
                item = existing with { CopiedAt = item.CopiedAt, SourceApp = item.SourceApp ?? existing.SourceApp, Origin = item.Origin };
            }
            _items.Insert(0, item);
            PruneLocked();
        }
        Commit();
    }

    /// <summary>Applies retention and the item limit to unpinned items. Caller holds the lock.</summary>
    private bool PruneLocked()
    {
        var options = Options;
        var cutoff = options.RetentionDays > 0 ? DateTimeOffset.Now.AddDays(-options.RetentionDays) : DateTimeOffset.MinValue;
        var max = Math.Max(1, options.MaxItems);
        var removed = new List<ClipItem>();
        var unpinned = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.IsPinned) continue;
            if (item.CopiedAt < cutoff || ++unpinned > max)
            {
                removed.Add(item);
                _items.RemoveAt(i--);
            }
        }
        if (removed.Count == 0) return false;
        DeleteOrphanedImagesLocked(removed);
        return true;
    }

    private void DeleteOrphanedImagesLocked(List<ClipItem> removed)
    {
        var inUse = new HashSet<string>(_items.Where(i => i.ImagePath is not null).Select(i => i.ImagePath!), StringComparer.OrdinalIgnoreCase);
        var paths = removed.Where(r => r.ImagePath is not null && !inUse.Contains(r.ImagePath))
            .SelectMany(r => new[] { r.ImagePath, r.ThumbnailPath }).OfType<string>().ToArray();
        if (paths.Length == 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (var p in paths)
            {
                try { File.Delete(p); }
                catch (Exception ex) { Log.Warn("Clipboard: could not delete an image file", ex); }
            }
        });
    }

    /// <summary>Publishes the snapshot, schedules a save and raises <see cref="Changed"/>.</summary>
    private void Commit()
    {
        lock (_gate)
        {
            Volatile.Write(ref _snapshot, [.. _items]);
            if (!_disposed)
            {
                _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }
        RaiseChanged();
    }

    private void SaveNow()
    {
        try { JsonStore.Save(HistoryPath, Volatile.Read(ref _snapshot)); }
        catch (Exception ex) { Log.Warn("Clipboard: saving the history failed", ex); }
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("Clipboard: Changed handler failed", ex); }
    }

    /// <summary>Pins or unpins an item.</summary>
    public void SetPinned(Guid id, bool pinned)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index < 0 || _items[index].IsPinned == pinned) return;
            _items[index] = _items[index] with { IsPinned = pinned };
            PruneLocked();
        }
        Commit();
    }

    /// <summary>Deletes one item.</summary>
    public void Delete(Guid id) => Delete([id]);

    /// <summary>Deletes several items.</summary>
    public void Delete(IReadOnlyList<Guid> ids)
    {
        var set = ids.ToHashSet();
        lock (_gate)
        {
            var removed = _items.Where(i => set.Contains(i.Id)).ToList();
            if (removed.Count == 0) return;
            _items.RemoveAll(i => set.Contains(i.Id));
            DeleteOrphanedImagesLocked(removed);
        }
        Commit();
    }

    /// <summary>Clears the history (pinned items stay unless <paramref name="keepPinned"/> is false).</summary>
    public void Clear(bool keepPinned = true)
    {
        lock (_gate)
        {
            var removed = _items.Where(i => !keepPinned || !i.IsPinned).ToList();
            if (removed.Count == 0) return;
            _items.RemoveAll(i => !keepPinned || !i.IsPinned);
            DeleteOrphanedImagesLocked(removed);
        }
        Commit();
    }

    // ───────────────────────────── copy / paste ─────────────────────────────

    /// <summary>Puts the items on the system clipboard and moves them to the top.</summary>
    public async Task<bool> CopyToClipboardAsync(IReadOnlyList<Guid> ids)
    {
        ClipItem[] items;
        lock (_gate)
        {
            items = ids.Select(id => _items.Find(i => i.Id == id)).OfType<ClipItem>().ToArray();
        }
        if (items.Length == 0) return false;

        byte[]? png = null;
        var image = items.FirstOrDefault(i => i.Kind == ClipKind.Image && i.ImagePath is not null);
        if (image is not null)
        {
            try { png = await File.ReadAllBytesAsync(image.ImagePath!).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warn("Clipboard: image file missing", ex); }
        }

        var files = items.Where(i => i.Kind == ClipKind.Files && i.Files is not null).SelectMany(i => i.Files!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var texts = items.Where(i => i.Kind is not (ClipKind.Files or ClipKind.Image) && i.Text is not null).Select(i => i.Text!).ToArray();
        var text = texts.Length > 0 ? string.Join(Environment.NewLine, texts) : files.Length > 0 ? string.Join(Environment.NewLine, files) : null;

        var ok = await SetSystemClipboardAsync(() =>
        {
            var data = new DataObject();
            if (files.Length > 0)
            {
                var collection = new StringCollection();
                collection.AddRange(files);
                data.SetFileDropList(collection);
            }
            if (png is not null)
            {
                data.SetData("PNG", new MemoryStream(png), false);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(png, writable: false);
                bmp.EndInit();
                bmp.Freeze();
                data.SetImage(bmp);
            }
            if (text is not null) data.SetText(text, TextDataFormat.UnicodeText);
            return data;
        }).ConfigureAwait(false);
        if (!ok) return false;

        var now = DateTimeOffset.Now;
        lock (_gate)
        {
            // First selected item ends on top.
            for (var k = items.Length - 1; k >= 0; k--)
            {
                var index = _items.FindIndex(i => i.Id == items[k].Id);
                if (index < 0) continue;
                var moved = _items[index] with { CopiedAt = now };
                _items.RemoveAt(index);
                _items.Insert(0, moved);
            }
        }
        Commit();
        return true;
    }

    /// <summary>Copies, collapses the notch, re-activates the previous app and sends Ctrl+V.</summary>
    public async Task<bool> PasteAsync(IReadOnlyList<Guid> ids)
    {
        if (!await CopyToClipboardAsync(ids).ConfigureAwait(false)) return false;
        AppServices.Shell?.Collapse();
        await Task.Delay(120).ConfigureAwait(false);
        var foreground = AppServices.Foreground;
        if (foreground is null) return true;
        foreground.RestorePreviousForeground();
        await Task.Delay(60).ConfigureAwait(false);
        foreground.SendPasteKeystroke();
        return true;
    }

    /// <summary>Adds text from elsewhere (phone) and optionally sets it on the system clipboard.</summary>
    public void AddExternal(string text, ClipOrigin origin, bool setSystemClipboard)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength) return;
        if (Options.Enabled)
        {
            AddOrPromote(new ClipItem
            {
                Id = Guid.NewGuid(), Kind = TextClassifier.Classify(text), Text = text,
                CopiedAt = DateTimeOffset.Now, Origin = origin,
            });
        }
        if (setSystemClipboard)
        {
            SetSystemClipboardAsync(() =>
            {
                var data = new DataObject();
                data.SetText(text, TextDataFormat.UnicodeText);
                return data;
            }).Forget("Clipboard external text");
        }
    }

    /// <summary>Current text on the system clipboard, if any.</summary>
    public async Task<string?> GetSystemClipboardTextAsync()
    {
        var lease = EnsureLease();
        if (lease is null) return null;
        return await lease.Thread.InvokeAsync(async () =>
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    return WpfClipboard.ContainsText() ? WpfClipboard.GetText() : null;
                }
                catch (Exception ex) when (ex is COMException or ExternalException)
                {
                    await Task.Delay(30 * (attempt + 1)).ConfigureAwait(true);
                }
            }
            return null;
        }).ConfigureAwait(false);
    }

    private async Task<bool> SetSystemClipboardAsync(Func<DataObject> build)
    {
        var lease = EnsureLease();
        if (lease is null) return false;
        return await lease.Thread.InvokeAsync(async () =>
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    WpfClipboard.SetDataObject(build(), copy: true);
                    _ignoreSequence = Win32.GetClipboardSequenceNumber();
                    return true;
                }
                catch (Exception ex) when (ex is COMException or ExternalException)
                {
                    await Task.Delay(30 * (attempt + 1)).ConfigureAwait(true);
                }
            }
            Log.Warn("Clipboard: could not write to the clipboard");
            return false;
        }).ConfigureAwait(false);
    }

    /// <summary>The STA lease; acquired lazily so copy/read also work before (or without) StartAsync.</summary>
    private StaThread.Lease? EnsureLease()
    {
        lock (_gate)
        {
            if (_lease is not null || _disposed) return _lease;
            try { _lease = StaThread.Acquire(); }
            catch (Exception ex) { Log.Error("Clipboard: STA thread unavailable", ex); }
            return _lease;
        }
    }

    /// <summary>Stops listening, flushes the history and releases the STA thread.</summary>
    public void Dispose()
    {
        StaThread.Lease? lease;
        bool save;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            lease = _lease;
            _lease = null;
            save = _saveTimer is not null;
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
        if (_started) _settings.Changed -= OnSettingsChanged;
        _hook?.Dispose();
        if (lease is not null)
        {
            var sta = lease.Thread;
            sta.Invoke(() =>
            {
                _readTimer?.Stop();
                if (_listening) Win32.RemoveClipboardFormatListener(sta.MessageWindow);
                _listening = false;
                return true;
            }, false, TimeSpan.FromSeconds(1));
            lease.Dispose();
        }
        if (save) SaveNow();
    }
}
