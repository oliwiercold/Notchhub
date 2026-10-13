using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotchHub.Demo.Imaging;

/// <summary>DrawingVisual → bitmap → PNG helpers. Must run on an STA thread (see <see cref="Infrastructure.StaWorker"/>).</summary>
internal static class PngRenderer
{
    /// <summary>Renders <paramref name="draw"/> into a frozen 96-DPI Pbgra32 bitmap.</summary>
    public static BitmapSource Render(int width, int height, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
        using (var dc = visual.RenderOpen()) draw(dc);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Renders and encodes as PNG (keeps alpha).</summary>
    public static byte[] RenderPng(int width, int height, Action<DrawingContext> draw) => Encode(Render(width, height, draw));

    /// <summary>High quality (Fant) downscale.</summary>
    public static BitmapSource Scale(BitmapSource source, int width, int height) =>
        Render(width, height, dc => dc.DrawImage(source, new Rect(0, 0, width, height)));

    /// <summary>Encodes as PNG; <paramref name="opaque"/> drops the alpha channel (smaller files for photos).</summary>
    public static byte[] Encode(BitmapSource source, bool opaque = false)
    {
        BitmapSource frame = opaque ? new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0) : source;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Writes the file atomically (temp file + rename) so readers never see a partial PNG.</summary>
    public static void WriteAtomic(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>Decodes a PNG/JPEG file into 32-bit BGRA pixels (row-major, little-endian ints = 0xAARRGGBB).</summary>
    public static (int Width, int Height, int[] Pixels) DecodeBgra(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        if (frame.Format != PixelFormats.Bgra32) frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        var pixels = new int[width * height];
        frame.CopyPixels(pixels, width * 4, 0);
        return (width, height, pixels);
    }

    // ───────── small drawing helpers shared by the painters ─────────

    public static Color Rgb(uint rgb, double alpha = 1.0) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static SolidColorBrush Solid(uint rgb, double alpha = 1.0) => Frozen(new SolidColorBrush(Rgb(rgb, alpha)));

    public static T Frozen<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }

    public static LinearGradientBrush Linear(Point start, Point end, params (double Offset, uint Rgb, double Alpha)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (offset, rgb, alpha) in stops) brush.GradientStops.Add(new GradientStop(Rgb(rgb, alpha), offset));
        return Frozen(brush);
    }

    public static RadialGradientBrush Radial(Point center, double radiusX, double radiusY, params (double Offset, uint Rgb, double Alpha)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radiusX,
            RadiusY = radiusY,
        };
        foreach (var (offset, rgb, alpha) in stops) brush.GradientStops.Add(new GradientStop(Rgb(rgb, alpha), offset));
        return Frozen(brush);
    }

    /// <summary>Soft "glow" blob: radial gradient that fades to transparent at the edge.</summary>
    public static void Blob(DrawingContext dc, Point center, double rx, double ry, uint rgb, double alpha)
    {
        var brush = Radial(new Point(0.5, 0.5), 0.5, 0.5, (0, rgb, alpha), (0.55, rgb, alpha * 0.55), (1, rgb, 0));
        dc.DrawEllipse(brush, null, center, rx, ry);
    }

    /// <summary>Smooth closed or open geometry through <paramref name="points"/> (Catmull–Rom converted to Béziers).</summary>
    public static StreamGeometry Smooth(IReadOnlyList<Point> points, bool closed, bool filled)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], filled, closed);
            for (var i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[Math.Max(i - 1, 0)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Math.Min(i + 2, points.Count - 1)];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                ctx.BezierTo(c1, c2, p2, true, true);
            }
        }
        return Frozen(geometry);
    }

    /// <summary>Polygon geometry (straight segments).</summary>
    public static StreamGeometry Polygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], true, true);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        return Frozen(geometry);
    }

    /// <summary>Text converted to a geometry centered on <paramref name="center"/> (exact ink bounds, not line metrics).</summary>
    public static Geometry CenteredText(string text, string fontFamily, FontWeight weight, double emSize, Point center)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal),
            emSize,
            Brushes.White,
            1.0);
        var glyphs = formatted.BuildGeometry(new Point(0, 0));
        var bounds = glyphs.Bounds;
        if (bounds.IsEmpty) return Geometry.Empty;
        var group = new GeometryGroup
        {
            Children = { glyphs },
            Transform = new TranslateTransform(center.X - bounds.X - bounds.Width / 2, center.Y - bounds.Y - bounds.Height / 2),
        };
        return Frozen(group);
    }
}
