using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Shell.Imaging;
using NotchHub.Platform.Shell.Internal;
using NotchHub.Platform.Shell.Internal.Interop;

namespace NotchHub.Platform.Shell.Wallpaper;

/// <summary>
/// Wallpaper of the notch's (primary) monitor via IDesktopWallpaper, with SystemParametersInfo / registry /
/// TranscodedWallpaper fallbacks. Changes arrive through WM_SETTINGCHANGE plus a cheap 30 s file-time check
/// (slideshows and Spotlight rewrite the file without a broadcast).
/// </summary>
public sealed class WallpaperService : IWallpaperService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly string TranscodedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");

    private readonly object _gate = new();
    private StaThread.Lease? _lease;
    private IDisposable? _hook;
    private Timer? _pollTimer;
    private Timer? _debounce;
    private Snapshot _current = new(null, null, null);
    private bool _disposed;

    private sealed record Snapshot(string? Path, uint? Solid, string? Signature);

    /// <summary>Image path of the wallpaper (null for solid color / unknown).</summary>
    public string? CurrentPath => Volatile.Read(ref _current).Path;

    /// <summary>ARGB color when the wallpaper is a solid color.</summary>
    public uint? SolidColor => Volatile.Read(ref _current).Solid;

    /// <summary>Wallpaper path, file content or solid color changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the current wallpaper and starts change detection.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() => Volatile.Write(ref _current, Read()), cancellationToken).ConfigureAwait(false);
        try
        {
            var lease = await StaThread.AcquireAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed) { lease.Dispose(); return; }
                _lease = lease;
                _hook = lease.Thread.AddMessageHandler(OnMessage);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Wallpaper: STA thread unavailable, relying on polling", ex);
        }
        lock (_gate)
        {
            if (_disposed) return;
            _debounce = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
            _pollTimer = new Timer(_ => Refresh(), null, PollInterval, PollInterval);
        }
        Log.Info($"Wallpaper: {(CurrentPath is not null ? "image" : SolidColor is not null ? "solid color" : "unknown")}");
    }

    /// <summary>Decodes the wallpaper scaled so the longest side ≤ <paramref name="maxSize"/>.</summary>
    public Task<WallpaperPixels?> GetPixelsAsync(int maxSize = 128, CancellationToken ct = default)
    {
        maxSize = Math.Clamp(maxSize, 1, 4096);
        var snap = Volatile.Read(ref _current);
        if (snap.Path is null && snap.Solid is null && snap.Signature is null) snap = Read();
        if (snap.Path is null)
        {
            if (snap.Solid is not uint solid) return Task.FromResult<WallpaperPixels?>(null);
            return Task.FromResult<WallpaperPixels?>(new WallpaperPixels(1, 1, [solid]));
        }
        var path = snap.Path;
        return ImagingThread.RunAsync(() => Decode(path, maxSize, ct), ct);
    }

    private static WallpaperPixels? Decode(string path, int maxSize, CancellationToken ct)
    {
        try
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (IOException) when (File.Exists(TranscodedPath) && path != TranscodedPath) { bytes = File.ReadAllBytes(TranscodedPath); }
            ct.ThrowIfCancellationRequested();

            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.DecodePixelWidth = Math.Min(maxSize * 4, 1024); // keeps memory small; the box filter does the rest
            image.EndInit();
            image.Freeze();

            BitmapSource source = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int w = source.PixelWidth, h = source.PixelHeight;
            var pixels = new byte[w * h * 4];
            source.CopyPixels(pixels, w * 4, 0);
            var bgra = new BgraImage(w, h, pixels);
            bgra.FixAllTransparent();
            ct.ThrowIfCancellationRequested();

            var (tw, th) = PixelScaler.FitWithin(w, h, maxSize);
            var scaled = tw == w && th == h ? bgra : PixelScaler.Downscale(bgra, tw, th);
            return new WallpaperPixels(scaled.Width, scaled.Height, scaled.ToArgb());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn("Wallpaper: decoding failed", ex);
            return null;
        }
    }

    private bool OnMessage(nint hwnd, int msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_SETTINGCHANGE || msg == Win32.WM_DISPLAYCHANGE || msg == Win32.WM_SYSCOLORCHANGE)
        {
            lock (_gate) _debounce?.Change(400, Timeout.Infinite);
        }
        return false;
    }

    private void Refresh()
    {
        if (_disposed) return;
        Snapshot next;
        try { next = Read(); }
        catch (Exception ex) { Log.Warn("Wallpaper: refresh failed", ex); return; }
        var prev = Interlocked.Exchange(ref _current, next);
        if (prev.Signature == next.Signature) return;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("Wallpaper: Changed handler failed", ex); }
    }

    private static Snapshot Read()
    {
        string? path = null;
        uint? solid = null;
        try
        {
            (path, solid) = ReadViaCom();
        }
        catch (Exception ex)
        {
            Log.Warn("Wallpaper: IDesktopWallpaper unavailable, using fallbacks", ex);
        }

        if (string.IsNullOrEmpty(path))
        {
            path = Win32.GetDeskWallpaperPath();
            if (string.IsNullOrEmpty(path))
            {
                using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                path = desktop?.GetValue("WallPaper") as string;
            }
        }
        if (!string.IsNullOrEmpty(path) && !File.Exists(path))
        {
            // Slideshows / Spotlight / deleted source files: Windows keeps a transcoded copy.
            path = File.Exists(TranscodedPath) ? TranscodedPath : null;
        }
        if (string.IsNullOrEmpty(path)) path = null;

        if (path is null && solid is null)
        {
            using var colors = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
            if (colors?.GetValue("Background") is string rgb)
            {
                var parts = rgb.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b))
                {
                    solid = 0xFF00_0000u | (uint)r << 16 | (uint)g << 8 | b;
                }
            }
        }
        if (path is not null) solid = null;

        string signature;
        if (path is not null)
        {
            var info = new FileInfo(path);
            var transcoded = new FileInfo(TranscodedPath);
            signature = $"{path}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}|{(info.Exists ? info.Length : 0)}|{(transcoded.Exists ? transcoded.LastWriteTimeUtc.Ticks : 0)}";
        }
        else
        {
            signature = $"solid|{solid}";
        }
        return new Snapshot(path, solid, signature);
    }

    private static (string? Path, uint? Solid) ReadViaCom()
    {
        var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
        try
        {
            using var dpi = Win32.PerMonitorDpiScope.Enter();
            var primary = Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY);
            Win32.TryGetMonitorRect(primary, out var primaryRect);
            string? monitorId = null;
            var count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var id = wallpaper.GetMonitorDevicePathAt(i);
                if (string.IsNullOrEmpty(id)) continue;
                try
                {
                    var rect = wallpaper.GetMonitorRECT(id);
                    if (rect.Left == primaryRect.Left && rect.Top == primaryRect.Top) { monitorId = id; break; }
                }
                catch (COMException)
                {
                    // detached monitor
                }
                monitorId ??= id;
            }

            string? path = null;
            try { path = monitorId is null ? null : wallpaper.GetWallpaper(monitorId); }
            catch (COMException) { }
            if (string.IsNullOrEmpty(path))
            {
                try { path = wallpaper.GetWallpaper(null); }
                catch (COMException) { } // S_FALSE-like failure when monitors differ
            }
            if (!string.IsNullOrEmpty(path)) return (path, null);

            var bgr = wallpaper.GetBackgroundColor(); // COLORREF 0x00BBGGRR
            var argb = 0xFF00_0000u | (bgr & 0xFF) << 16 | (bgr & 0xFF00) | (bgr >> 16 & 0xFF);
            return (null, argb);
        }
        finally
        {
            Marshal.FinalReleaseComObject(wallpaper);
        }
    }

    /// <summary>Stops change detection.</summary>
    public void Dispose()
    {
        StaThread.Lease? lease;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer?.Dispose();
            _debounce?.Dispose();
            _hook?.Dispose();
            lease = _lease;
            _lease = null;
        }
        lease?.Dispose();
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
    }
}
