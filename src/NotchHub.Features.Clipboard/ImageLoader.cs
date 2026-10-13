using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchHub.Core;

namespace NotchHub.Features.Clipboard;

/// <summary>
/// Decodes images off the UI thread at a small size (DecodePixelWidth), freezes and caches them.
/// Two LRU caches: many tiny row thumbnails, and only a handful of large preview-size decodes.
/// </summary>
internal static class ImageLoader
{
    /// <summary>Decodes up to this width count as thumbnails (64 px rows); anything larger goes to the small preview cache.</summary>
    private const int ThumbMaxWidth = 128;
    private static readonly Lru Thumbs = new(160);
    private static readonly Lru Previews = new(6);

    public static bool TryGetCached(string path, int decodeWidth, out ImageSource? image)
    {
        image = null;
        if (CacheFor(decodeWidth).TryGet(Key(path, decodeWidth)) is { IsCompletedSuccessfully: true } t)
        {
            image = t.Result;
            return true;
        }
        return false;
    }

    public static Task<ImageSource?> LoadAsync(string path, int decodeWidth) =>
        CacheFor(decodeWidth).GetOrAdd(Key(path, decodeWidth), () => Task.Run(() => Decode(path, decodeWidth)));

    /// <summary>Drops the large preview decodes (tab hidden).</summary>
    public static void ClearPreviews() => Previews.Clear();

    /// <summary>Drops everything (view released).</summary>
    public static void ClearAll()
    {
        Previews.Clear();
        Thumbs.Clear();
    }

    private static Lru CacheFor(int decodeWidth) => decodeWidth > 0 && decodeWidth <= ThumbMaxWidth ? Thumbs : Previews;

    private static string Key(string path, int w) => w + "|" + path.ToUpperInvariant();

    private static ImageSource? Decode(string path, int decodeWidth)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard: image decode failed: " + ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Tiny thread-safe LRU of decode tasks; evicts the least recently used entry on insert.</summary>
    private sealed class Lru(int capacity)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, LinkedListNode<(string Key, Task<ImageSource?> Task)>> _map = new(StringComparer.Ordinal);
        private readonly LinkedList<(string Key, Task<ImageSource?> Task)> _order = new();

        public Task<ImageSource?>? TryGet(string key)
        {
            lock (_gate)
            {
                if (!_map.TryGetValue(key, out var node)) return null;
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Task;
            }
        }

        public Task<ImageSource?> GetOrAdd(string key, Func<Task<ImageSource?>> factory)
        {
            lock (_gate)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    return node.Value.Task;
                }
                var task = factory();
                _map[key] = _order.AddFirst((key, task));
                while (_map.Count > capacity && _order.Last is { } last)
                {
                    _order.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
                return task;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _map.Clear();
                _order.Clear();
            }
        }
    }
}
