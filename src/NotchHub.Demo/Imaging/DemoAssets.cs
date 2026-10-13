using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Infrastructure;

namespace NotchHub.Demo.Imaging;

/// <summary>Generated album / video artwork used by the media fake.</summary>
internal enum AlbumArt { LofiStudy, RainyWindow, NightDrive, VideoThumbnail }

/// <summary>A generated clipboard picture on disk (full size + thumbnail).</summary>
internal sealed record ClipImageFiles(string ImagePath, string ThumbnailPath, int Width, int Height);

/// <summary>
/// Single entry point for every generated image. Rendering happens once on the STA imaging thread
/// (<see cref="StaWorker"/>); results are memoized (bytes in memory, pictures as PNG files under
/// <c>AppPaths.Dir("demo")</c>, reused across runs). Failures are logged and yield null — callers degrade to "no image".
/// </summary>
internal static class DemoAssets
{
    public const int AlbumArtSize = 256;
    public const int WallpaperWidth = 1920;
    public const int WallpaperHeight = 1080;
    private const int ThumbnailMaxSide = 320; // ClipItem.ThumbnailPath contract

    // Bump when a painter changes so stale files from older runs are not reused.
    private const string Version = "v1";

    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> Bytes = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Files = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<ClipImageFiles?>>> Clips = new();
    private static readonly ConcurrentDictionary<int, Lazy<Task<WallpaperPixels?>>> Pixels = new();

    /// <summary>Folder of the generated files.</summary>
    public static string Directory => AppPaths.Dir("demo");

    /// <summary>Cache key that changes whenever the artwork changes (for <see cref="MediaSessionInfo.ThumbnailKey"/>).</summary>
    public static string ArtKey(AlbumArt art) => $"demo-art-{art}-{Version}".ToLowerInvariant();

    /// <summary>PNG of a generated app icon.</summary>
    public static Task<byte[]?> IconAsync(IconSpec spec) => Memo(Bytes, spec.CacheKey, () => IconPainter.RenderPng(spec));

    /// <summary>PNG of generated album art.</summary>
    public static Task<byte[]?> AlbumArtAsync(AlbumArt art) =>
        Memo(Bytes, ArtKey(art), () => PngRenderer.Encode(AlbumArtPainter.Render(art, AlbumArtSize), opaque: true));

    /// <summary>Path of the generated "landscape at golden hour" wallpaper (1920×1080 PNG).</summary>
    public static Task<string?> WallpaperAsync() =>
        Memo(Files, "wallpaper", () => EnsureFile($"wallpaper-golden-hour-{Version}.png",
            () => PngRenderer.Render(WallpaperWidth, WallpaperHeight, dc => WallpaperPainter.Draw(dc, WallpaperWidth, WallpaperHeight))));

    /// <summary>A 1920×1080 "screenshot" of a code editor (clipboard image item).</summary>
    public static Task<ClipImageFiles?> ScreenshotClipAsync() =>
        Memo(Clips, "clip-screenshot", () => EnsureClip($"clip-screenshot-1920x1080-{Version}", 1920, 1080,
            dc => ScreenshotPainter.Draw(dc, 1920, 1080)));

    /// <summary>A 1280×720 "photo" (close-up of the landscape) for an older clipboard image item.</summary>
    public static Task<ClipImageFiles?> PhotoClipAsync() =>
        Memo(Clips, "clip-photo", () => EnsureClip($"clip-photo-1280x720-{Version}", 1280, 720, dc =>
        {
            // Zoom into the right third of the landscape (lone tree + golden water).
            dc.PushTransform(new MatrixTransform(1.9, 0, 0, 1.9, -1150, -330));
            WallpaperPainter.Draw(dc, 1280, 720);
            dc.Pop();
        }));

    /// <summary>Wallpaper pixels downscaled so the longest side ≤ <paramref name="maxSize"/>.</summary>
    public static async Task<WallpaperPixels?> WallpaperPixelsAsync(int maxSize)
    {
        maxSize = Math.Clamp(maxSize, 1, Math.Max(WallpaperWidth, WallpaperHeight));
        var path = await WallpaperAsync().ConfigureAwait(false);
        if (path is null) return null;
        var pixels = await Memo(Pixels, maxSize, () => Decode(path, maxSize)).ConfigureAwait(false);
        // Hand out a private copy: the array inside the record is mutable.
        return pixels is null ? null : pixels with { Argb = (uint[])pixels.Argb.Clone() };
    }

    /// <summary>Starts rendering everything in the background (fire and forget).</summary>
    public static void WarmUp()
    {
        Task.Run(async () =>
        {
            var tasks = new List<Task> { WallpaperAsync() };
            tasks.AddRange(Enum.GetValues<AlbumArt>().Select(AlbumArtAsync));
            tasks.AddRange(DemoIcons.All.Select(IconAsync));
            tasks.Add(ScreenshotClipAsync());
            tasks.Add(PhotoClipAsync());
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }).Forget("Demo asset warm-up");
    }

    private static Task<T?> Memo<TKey, T>(ConcurrentDictionary<TKey, Lazy<Task<T?>>> cache, TKey key, Func<T> renderOnSta)
        where TKey : notnull
        where T : class
    {
        var lazy = cache.GetOrAdd(key, k => new Lazy<Task<T?>>(() => RunSafe(cache, k, renderOnSta)));
        return lazy.Value;
    }

    private static async Task<T?> RunSafe<TKey, T>(ConcurrentDictionary<TKey, Lazy<Task<T?>>> cache, TKey key, Func<T> renderOnSta)
        where TKey : notnull
        where T : class
    {
        try
        {
            return await StaWorker.InvokeAsync(renderOnSta).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error($"Demo asset '{key}' could not be generated", ex);
            cache.TryRemove(key, out _); // allow a later retry (e.g. transient file lock)
            return null;
        }
    }

    private static string EnsureFile(string fileName, Func<BitmapSource> render)
    {
        var path = Path.Combine(Directory, fileName);
        if (!IsUsable(path)) PngRenderer.WriteAtomic(path, PngRenderer.Encode(render(), opaque: true));
        return path;
    }

    private static ClipImageFiles EnsureClip(string baseName, int width, int height, Action<DrawingContext> draw)
    {
        var imagePath = Path.Combine(Directory, baseName + ".png");
        var thumbPath = Path.Combine(Directory, baseName + "-thumb.png");
        if (!IsUsable(imagePath) || !IsUsable(thumbPath))
        {
            var image = PngRenderer.Render(width, height, draw);
            var scale = Math.Min(1.0, ThumbnailMaxSide / (double)Math.Max(width, height));
            var thumb = PngRenderer.Scale(image, Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
            PngRenderer.WriteAtomic(imagePath, PngRenderer.Encode(image, opaque: true));
            PngRenderer.WriteAtomic(thumbPath, PngRenderer.Encode(thumb, opaque: true));
        }
        return new ClipImageFiles(imagePath, thumbPath, width, height);
    }

    private static bool IsUsable(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch (IOException) { return false; }
    }

    private static WallpaperPixels Decode(string path, int maxSize)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // read fully so the file is not kept open
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        var scale = Math.Min(1.0, maxSize / (double)Math.Max(WallpaperWidth, WallpaperHeight));
        bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Round(WallpaperWidth * scale));
        bitmap.DecodePixelHeight = Math.Max(1, (int)Math.Round(WallpaperHeight * scale));
        bitmap.EndInit();
        bitmap.Freeze();

        BitmapSource frame = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        var argb = new uint[width * height];
        frame.CopyPixels(argb, width * 4, 0); // little-endian BGRA bytes == 0xAARRGGBB words
        return new WallpaperPixels(width, height, argb);
    }
}
