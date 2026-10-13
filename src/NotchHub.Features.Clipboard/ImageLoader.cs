using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchHub.Core;

namespace NotchHub.Features.Clipboard;

/// <summary>Decodes images off the UI thread at a small size (DecodePixelWidth), freezes and caches them.</summary>
internal static class ImageLoader
{
    private const int MaxEntries = 160;
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCached(string path, int decodeWidth, out ImageSource? image)
    {
        image = null;
        if (Cache.TryGetValue(Key(path, decodeWidth), out var t) && t.IsCompletedSuccessfully)
        {
            image = t.Result;
            return true;
        }
        return false;
    }

    public static Task<ImageSource?> LoadAsync(string path, int decodeWidth)
    {
        if (Cache.Count > MaxEntries) Cache.Clear();
        return Cache.GetOrAdd(Key(path, decodeWidth), _ => Task.Run(() => Decode(path, decodeWidth)));
    }

    private static string Key(string path, int w) => w + "|" + path;

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
}
