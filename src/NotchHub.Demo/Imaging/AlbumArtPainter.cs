using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static NotchHub.Demo.Imaging.PngRenderer;

namespace NotchHub.Demo.Imaging;

/// <summary>Paints square cover art: soft gradients with a few subtle shapes (looks like lo-fi artwork when small).</summary>
internal static class AlbumArtPainter
{
    public static BitmapSource Render(AlbumArt art, int size) => PngRenderer.Render(size, size, dc =>
    {
        switch (art)
        {
            case AlbumArt.RainyWindow: RainyWindow(dc, size); break;
            case AlbumArt.NightDrive: NightDrive(dc, size); break;
            case AlbumArt.VideoThumbnail: VideoThumbnail(dc, size); break;
            default: LofiStudy(dc, size); break;
        }
    });

    /// <summary>Purple → magenta haze, a pale moon, rolling hills and a desk-lamp glow (the "lofi radio" cover).</summary>
    private static void LofiStudy(DrawingContext dc, double s)
    {
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(1, 1), (0, 0x5B3A9E, 1), (0.55, 0x8E4AA8, 1), (1, 0xC9558C, 1)), null, new Rect(0, 0, s, s));
        Blob(dc, new Point(s * 0.78, s * 0.22), s * 0.55, s * 0.45, 0xF27BB0, 0.45);
        Blob(dc, new Point(s * 0.12, s * 0.85), s * 0.60, s * 0.45, 0x3A1F6E, 0.55);

        // Moon with a soft halo.
        Blob(dc, new Point(s * 0.68, s * 0.30), s * 0.24, s * 0.24, 0xFFD9EC, 0.35);
        dc.DrawEllipse(Solid(0xFFE6F2, 0.55), null, new Point(s * 0.68, s * 0.30), s * 0.085, s * 0.085);

        // Stars.
        foreach (var (x, y, r) in new[] { (0.18, 0.16, 0.008), (0.32, 0.09, 0.006), (0.44, 0.22, 0.005), (0.86, 0.48, 0.006), (0.25, 0.36, 0.004) })
            dc.DrawEllipse(Solid(0xFFFFFF, 0.55), null, new Point(s * x, s * y), s * r, s * r);

        // Two layers of hills.
        Hills(dc, s, 0.70, 0.06, 0x4A2378, 0.55, phase: 0.2);
        Hills(dc, s, 0.80, 0.05, 0x2E1250, 0.70, phase: 1.3);

        // Thin ring (vinyl hint) + warm lamp glow.
        dc.DrawEllipse(null, Frozen(new Pen(Solid(0xFFFFFF, 0.10), s * 0.012)), new Point(s * 0.30, s * 0.62), s * 0.16, s * 0.16);
        Blob(dc, new Point(s * 0.86, s * 0.86), s * 0.22, s * 0.18, 0xFFB38A, 0.30);
    }

    /// <summary>Night city behind a rainy window (teal / navy).</summary>
    private static void RainyWindow(DrawingContext dc, double s)
    {
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(0.3, 1), (0, 0x0E2340, 1), (0.6, 0x16495C, 1), (1, 0x1F6F78, 1)), null, new Rect(0, 0, s, s));
        Blob(dc, new Point(s * 0.30, s * 0.30), s * 0.50, s * 0.40, 0x3FA7B5, 0.25);

        // Skyline blocks with lit windows.
        var rng = new Random(11);
        double x = 0;
        while (x < s)
        {
            var w = s * (0.08 + rng.NextDouble() * 0.10);
            var h = s * (0.18 + rng.NextDouble() * 0.30);
            dc.DrawRectangle(Solid(0x0A1A2B, 0.85), null, new Rect(x, s - h, w - s * 0.01, h));
            for (var i = 0; i < 4; i++)
            {
                var wx = x + s * 0.015 + rng.NextDouble() * (w - s * 0.05);
                var wy = s - h + s * 0.03 + rng.NextDouble() * (h - s * 0.06);
                dc.DrawRectangle(Solid(0xFFC86B, 0.75), null, new Rect(wx, wy, s * 0.014, s * 0.018));
            }
            x += w;
        }

        // Bokeh lights and rain streaks.
        foreach (var (bx, by, br, c) in new[] { (0.20, 0.55, 0.06, 0xFFB86B), (0.70, 0.48, 0.05, 0x7FE3E0), (0.85, 0.66, 0.07, 0xFF8FA3) })
            Blob(dc, new Point(s * bx, s * by), s * br, s * br, (uint)c, 0.55);
        var rain = Frozen(new Pen(Solid(0xCFEFF5, 0.16), s * 0.004) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        for (var i = 0; i < 40; i++)
        {
            var rx = rng.NextDouble() * s;
            var ry = rng.NextDouble() * s;
            dc.DrawLine(rain, new Point(rx, ry), new Point(rx - s * 0.02, ry + s * 0.09));
        }
    }

    /// <summary>Retro sunset: striped sun over a perspective grid (orange → pink → violet).</summary>
    private static void NightDrive(DrawingContext dc, double s)
    {
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(0, 1), (0, 0x2B1555, 1), (0.45, 0xC2457E, 1), (0.62, 0xFF8A4C, 1), (1, 0x3A1A4F, 1)), null, new Rect(0, 0, s, s));
        Blob(dc, new Point(s * 0.5, s * 0.55), s * 0.60, s * 0.35, 0xFFB36B, 0.35);

        // Sun with horizontal cut-outs.
        var sun = new Rect(s * 0.27, s * 0.22, s * 0.46, s * 0.46);
        Geometry disc = new EllipseGeometry(sun);
        var band = s * 0.022;
        for (var i = 0; i < 5; i++)
        {
            var y = s * 0.47 + i * band * 2.1;
            disc = Geometry.Combine(disc, new RectangleGeometry(new Rect(0, y, s, band * (0.5 + i * 0.25))), GeometryCombineMode.Exclude, null);
        }
        dc.DrawGeometry(Linear(new Point(0, 0), new Point(0, 1), (0, 0xFFE08A, 1), (1, 0xFF6F91, 1)), null, Frozen(disc));

        // Ground with a perspective grid.
        var horizon = s * 0.66;
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(0, 1), (0, 0x2A0F45, 1), (1, 0x12061F, 1)), null, new Rect(0, horizon, s, s - horizon));
        var grid = Frozen(new Pen(Solid(0xFF7AC0, 0.45), s * 0.005));
        for (var i = -6; i <= 6; i++) dc.DrawLine(grid, new Point(s * 0.5 + i * s * 0.02, horizon), new Point(s * 0.5 + i * s * 0.22, s));
        for (var i = 1; i <= 6; i++)
        {
            var y = horizon + (s - horizon) * Math.Pow(i / 6.0, 1.8);
            dc.DrawLine(grid, new Point(0, y), new Point(s, y));
        }
    }

    /// <summary>Video thumbnail for the paused browser session: dark card, bold "M3" title and expressive shapes.</summary>
    private static void VideoThumbnail(DrawingContext dc, double s)
    {
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(1, 1), (0, 0x161320, 1), (1, 0x2E1F45, 1)), null, new Rect(0, 0, s, s));
        Blob(dc, new Point(s * 0.80, s * 0.20), s * 0.45, s * 0.40, 0x7C4DFF, 0.45);
        Blob(dc, new Point(s * 0.15, s * 0.90), s * 0.40, s * 0.30, 0xFF5277, 0.35);

        // Expressive shapes: a pill, a cookie-like flower and a circle.
        dc.DrawRoundedRectangle(Solid(0xD0BCFF), null, new Rect(s * 0.60, s * 0.62, s * 0.30, s * 0.14), s * 0.07, s * 0.07);
        dc.DrawGeometry(Solid(0xFFB4A8), null, Flower(new Point(s * 0.78, s * 0.33), s * 0.12, s * 0.09, 8));
        dc.DrawEllipse(Solid(0x9DE0B8), null, new Point(s * 0.60, s * 0.30), s * 0.05, s * 0.05);

        dc.DrawGeometry(Solid(0xFFFFFF), null, CenteredText("M3", "Segoe UI", FontWeights.Black, s * 0.30, new Point(s * 0.27, s * 0.40)));
        dc.DrawGeometry(Solid(0xFFFFFF, 0.75), null, CenteredText("EXPRESSIVE", "Segoe UI", FontWeights.Bold, s * 0.065, new Point(s * 0.27, s * 0.60)));

        // Progress bar of a partially watched video.
        dc.DrawRectangle(Solid(0xFFFFFF, 0.25), null, new Rect(0, s * 0.965, s, s * 0.035));
        dc.DrawRectangle(Solid(0xFF0033), null, new Rect(0, s * 0.965, s * 0.35, s * 0.035));
    }

    private static void Hills(DrawingContext dc, double s, double baseline, double amplitude, uint color, double alpha, double phase)
    {
        var points = new List<Point>();
        for (var i = 0; i <= 8; i++)
        {
            var x = i / 8.0;
            points.Add(new Point(s * x, s * (baseline - amplitude * Math.Sin(x * Math.PI * 1.6 + phase))));
        }
        points.Add(new Point(s, s));
        points.Add(new Point(0, s));
        dc.DrawGeometry(Solid(color, alpha), null, Smooth(points, closed: true, filled: true));
    }

    private static Geometry Flower(Point center, double outer, double inner, int petals)
    {
        var points = new List<Point>();
        const int steps = 64;
        for (var i = 0; i < steps; i++)
        {
            var a = i * 2 * Math.PI / steps;
            var r = inner + (outer - inner) * (0.5 + 0.5 * Math.Cos(a * petals));
            points.Add(new Point(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a)));
        }
        points.Add(points[0]);
        return Smooth(points, closed: true, filled: true);
    }
}
