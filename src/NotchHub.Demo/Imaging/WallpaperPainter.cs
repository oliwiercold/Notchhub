using System.Windows;
using System.Windows.Media;
using static NotchHub.Demo.Imaging.PngRenderer;

namespace NotchHub.Demo.Imaging;

/// <summary>
/// Paints the demo wallpaper: rice terraces at golden hour (after the reference photo) — blue sky and hazy mountains on
/// the left, a warm golden haze with a lone tree on the right, and reflective water terraces at the bottom.
/// The warm, high-chroma gold covers most of the picture so wallpaper-derived theming yields a gold accent.
/// Vector based: renders at any size (also used zoomed in for a clipboard "photo").
/// </summary>
internal static class WallpaperPainter
{
    public static void Draw(DrawingContext dc, double width, double height)
    {
        var p = new Canvas(width, height);
        var rng = new Random(1920); // fixed: identical picture every run

        Sky(dc, p);
        Clouds(dc, p, rng);
        Mountains(dc, p);
        Water(dc, p);
        Terraces(dc, p);
        RightHill(dc, p, rng);
        LeftTreeLine(dc, p, rng);
        Foreground(dc, p);
        Grade(dc, p);
    }

    private static void Sky(DrawingContext dc, Canvas p)
    {
        dc.DrawRectangle(Linear(new Point(0, 0.2), new Point(1, 0.45),
            (0.00, 0x4C7DB6, 1), (0.30, 0x8FAFCF, 1), (0.50, 0xCFCDBD, 1), (0.68, 0xE9D29A, 1), (1.00, 0xF0C468, 1)), null, p.All);
        // Deeper blue in the top-left corner.
        Blob(dc, p.P(0.0, 0.0), p.W * 0.62, p.H * 0.62, 0x3566A3, 0.60);
        // Sun glow behind the haze on the right.
        Blob(dc, p.P(0.80, 0.40), p.W * 0.50, p.H * 0.62, 0xFFE9B0, 0.85);
        Blob(dc, p.P(0.82, 0.42), p.W * 0.22, p.H * 0.30, 0xFFF6DC, 0.80);
        // Bright haze along the horizon.
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(0, 1), (0, 0xF6E6C2, 0), (0.6, 0xF6E6C2, 0.45), (1, 0xF6E6C2, 0)), null,
            new Rect(0, p.H * 0.22, p.W, p.H * 0.36));
    }

    private static void Clouds(DrawingContext dc, Canvas p, Random rng)
    {
        // Cumulus clusters drifting from the top-left towards the middle; warmer and fainter towards the sun.
        (double U, double V, double Size, uint Color, double Alpha)[] clusters =
        [
            (0.10, 0.13, 1.00, 0xFFFFFF, 0.55), (0.22, 0.07, 0.80, 0xFFFFFF, 0.50), (0.31, 0.17, 1.10, 0xF7F4EE, 0.55),
            (0.40, 0.08, 0.85, 0xFBF3E3, 0.45), (0.48, 0.20, 0.90, 0xFFF1D6, 0.40), (0.58, 0.10, 0.75, 0xFFEBC4, 0.30),
            (0.68, 0.18, 0.70, 0xFFE7BC, 0.25), (0.05, 0.26, 0.70, 0xEEF2F7, 0.40), (0.36, 0.28, 0.60, 0xF7EEDD, 0.30),
        ];
        foreach (var (u, v, size, color, alpha) in clusters)
        {
            for (var i = 0; i < 7; i++)
            {
                var du = (rng.NextDouble() - 0.5) * 0.09 * size;
                var dv = (rng.NextDouble() - 0.5) * 0.05 * size;
                var rx = p.W * (0.035 + rng.NextDouble() * 0.035) * size;
                var ry = rx * (0.55 + rng.NextDouble() * 0.25);
                Blob(dc, p.P(u + du, v + dv), rx, ry, color, alpha * (0.6 + rng.NextDouble() * 0.4));
            }
            // Shaded underside gives the clusters some volume.
            Blob(dc, p.P(u, v + 0.035 * size), p.W * 0.06 * size, p.H * 0.03 * size, 0x8C9DB5, 0.18);
        }
    }

    private static void Mountains(DrawingContext dc, Canvas p)
    {
        // Far ridge: hazy blue, sloping from the top-left down into the valley.
        var far = p.Path(closed: true, (0.00, 0.33), (0.06, 0.315), (0.13, 0.35), (0.22, 0.39), (0.30, 0.43), (0.40, 0.48),
            (0.50, 0.54), (0.58, 0.58), (0.58, 0.70), (0.00, 0.70));
        dc.DrawGeometry(Linear(new Point(0, 0), new Point(0, 1), (0, 0x58719A, 0.95), (1, 0x9AA8B8, 0.55)), null, far);

        // Nearer, darker ridge.
        var near = p.Path(closed: true, (0.00, 0.47), (0.08, 0.49), (0.18, 0.53), (0.30, 0.57), (0.40, 0.60), (0.40, 0.70), (0.00, 0.70));
        dc.DrawGeometry(Linear(new Point(0, 0), new Point(0, 1), (0, 0x40597A, 0.90), (1, 0x5E7088, 0.70)), null, near);

        // Faint golden hills in the haze on the right.
        var right = p.Path(closed: true, (0.52, 0.57), (0.62, 0.54), (0.74, 0.52), (0.86, 0.50), (1.00, 0.49), (1.00, 0.62), (0.52, 0.62));
        dc.DrawGeometry(Solid(0xD9B26A, 0.45), null, right);
    }

    private static void Water(DrawingContext dc, Canvas p)
    {
        // Terrace basins mirror the sky: pale blue-white on the left, molten gold under the sun.
        var basin = p.Path(closed: true, (0.00, 0.62), (0.20, 0.61), (0.40, 0.585), (0.60, 0.555), (0.80, 0.54), (1.00, 0.535), (1.00, 1.00), (0.00, 1.00));
        dc.DrawGeometry(Linear(new Point(0, 0.3), new Point(1, 0.7),
            (0.00, 0xC5D0DC, 1), (0.35, 0xDCDCD2, 1), (0.60, 0xEFD596, 1), (0.85, 0xF4C35C, 1), (1.00, 0xE9AE45, 1)), null, basin);
        Blob(dc, p.P(0.83, 0.70), p.W * 0.30, p.H * 0.22, 0xFFE6A6, 0.65); // sun reflection
        Blob(dc, p.P(0.79, 0.64), p.W * 0.04, p.H * 0.06, 0x2B2A1C, 0.30); // tree reflection
        Blob(dc, p.P(0.25, 0.78), p.W * 0.30, p.H * 0.18, 0xF2F4F7, 0.35); // cloud reflection
    }

    private static void Terraces(DrawingContext dc, Canvas p)
    {
        // Each terrace edge is a mud ridge: a curve fanning out from the far right (near the horizon) to the lower left.
        const int count = 14;
        for (var k = 0; k < count; k++)
        {
            var t = k / (double)(count - 1);
            var left = 0.63 + t * 0.62;
            var right = 0.545 + Math.Pow(t, 1.25) * 0.50;
            var points = new List<Point>();
            for (var i = 0; i <= 24; i++)
            {
                var u = i / 24.0;
                var ease = u * u * (3 - 2 * u);
                var v = left + (right - left) * ease
                        + 0.010 * Math.Sin(2 * Math.PI * (u * 2.3 + t * 1.7))
                        + 0.006 * Math.Sin(2 * Math.PI * (u * 5.2 + t * 3.1));
                points.Add(p.P(u, v));
            }
            var curve = Smooth(points, closed: false, filled: false);
            var thickness = p.H * (0.0035 + 0.011 * t * t);
            // Soft muddy shoulder below the ridge, then the dark ridge itself.
            var shoulder = Frozen(new Pen(Solid(0x6B5A3A, 0.35), thickness * 2.4));
            dc.PushTransform(new TranslateTransform(0, thickness * 1.3));
            dc.DrawGeometry(null, shoulder, curve);
            dc.Pop();
            dc.DrawGeometry(null, Frozen(new Pen(Solid(0x2A2418, 0.92), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), curve);
        }
    }

    private static void RightHill(DrawingContext dc, Canvas p, Random rng)
    {
        var hill = p.Path(closed: true, (0.66, 0.545), (0.70, 0.515), (0.76, 0.500), (0.86, 0.490), (1.00, 0.482), (1.00, 0.560), (0.66, 0.560));
        dc.DrawGeometry(Linear(new Point(0, 0), new Point(0, 1), (0, 0x3A4426, 1), (1, 0x232617, 1)), null, hill);

        // Bushes along the crest.
        for (var i = 0; i < 26; i++)
        {
            var u = 0.68 + rng.NextDouble() * 0.32;
            var v = 0.505 - (u - 0.68) * 0.06 + rng.NextDouble() * 0.01;
            var r = p.H * (0.006 + rng.NextDouble() * 0.010);
            dc.DrawEllipse(Solid(0x1F2414, 0.95), null, p.P(u, v), r * 1.3, r);
        }

        // The lone tree: thin trunk and a wide, flat canopy.
        dc.DrawRectangle(Solid(0x16190F), null, new Rect(p.X(0.797), p.Y(0.335), p.W * 0.0045, p.H * 0.165));
        (double U, double V, double Rx, double Ry)[] canopy =
        [
            (0.800, 0.318, 0.042, 0.030), (0.775, 0.330, 0.028, 0.020), (0.826, 0.328, 0.028, 0.021),
            (0.792, 0.300, 0.026, 0.022), (0.812, 0.302, 0.024, 0.020), (0.800, 0.338, 0.030, 0.014),
        ];
        foreach (var (u, v, rx, ry) in canopy) dc.DrawEllipse(Solid(0x1B2013), null, p.P(u, v), p.W * rx, p.H * ry * 1.6);

        // A slim second tree and a small one further right.
        dc.DrawRectangle(Solid(0x16190F), null, new Rect(p.X(0.929), p.Y(0.38), p.W * 0.003, p.H * 0.11));
        for (var i = 0; i < 6; i++)
            dc.DrawEllipse(Solid(0x1B2013), null, p.P(0.930, 0.365 + i * 0.018), p.W * (0.010 + i * 0.002), p.H * 0.016);
        dc.DrawEllipse(Solid(0x1B2013), null, p.P(0.865, 0.470), p.W * 0.010, p.H * 0.022);
    }

    private static void LeftTreeLine(DrawingContext dc, Canvas p, Random rng)
    {
        for (var i = 0; i < 40; i++)
        {
            var u = rng.NextDouble() * 0.36;
            var v = 0.585 + u * 0.03 + rng.NextDouble() * 0.04;
            var r = p.H * (0.008 + rng.NextDouble() * 0.016);
            dc.DrawEllipse(Solid(0x1E2A1C, 0.92), null, p.P(u, v), r, r * 1.25);
        }
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(0, 1), (0, 0x1E2A1C, 0.85), (1, 0x1E2A1C, 0)), null,
            new Rect(0, p.Y(0.60), p.W * 0.36, p.H * 0.05));
    }

    private static void Foreground(DrawingContext dc, Canvas p)
    {
        // Grassy bank in the bottom-right corner.
        var bank = p.Path(closed: true, (0.58, 1.00), (0.70, 0.95), (0.80, 0.90), (0.90, 0.86), (1.00, 0.82), (1.00, 1.00));
        dc.DrawGeometry(Linear(new Point(0, 0), new Point(0, 1), (0, 0x4A4A26, 1), (1, 0x22220F, 1)), null, bank);
        var rim = p.Path(closed: false, (0.58, 1.00), (0.70, 0.95), (0.80, 0.90), (0.90, 0.86), (1.00, 0.82));
        dc.DrawGeometry(null, Frozen(new Pen(Solid(0x6E7A33, 0.55), p.H * 0.008)), rim);
    }

    private static void Grade(DrawingContext dc, Canvas p)
    {
        // Warm cast towards the right, darker bottom edge and a gentle vignette.
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(1, 0), (0, 0xF2C35A, 0), (1, 0xF2B94A, 0.14)), null, p.All);
        dc.DrawRectangle(Linear(new Point(0, 0), new Point(0, 1), (0.80, 0x000000, 0), (1, 0x000000, 0.28)), null, p.All);
        dc.DrawRectangle(Radial(new Point(0.5, 0.45), 0.75, 0.75, (0.6, 0x000000, 0), (1, 0x000000, 0.30)), null, p.All);
    }

    /// <summary>Maps normalized (u, v) coordinates to pixels.</summary>
    private readonly struct Canvas(double width, double height)
    {
        public double W { get; } = width;
        public double H { get; } = height;
        public Rect All => new(0, 0, W, H);
        public double X(double u) => u * W;
        public double Y(double v) => v * H;
        public Point P(double u, double v) => new(u * W, v * H);

        public StreamGeometry Path(bool closed, params (double U, double V)[] points) =>
            Smooth(Array.ConvertAll(points, ToPoint), closed, filled: closed);

        private Point ToPoint((double U, double V) q) => P(q.U, q.V);
    }
}
