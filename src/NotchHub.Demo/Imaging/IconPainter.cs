using System.Windows;
using System.Windows.Media;
using static NotchHub.Demo.Imaging.PngRenderer;

namespace NotchHub.Demo.Imaging;

/// <summary>Outline of a generated icon.</summary>
internal enum IconShape { Circle, Squircle }

/// <summary>What is drawn on top of the colored background.</summary>
internal enum IconGlyph { Text, ChatBubble, Envelope, Play, CalendarPage, Cloud, Bag }

/// <summary>Description of a generated app icon (also its cache key).</summary>
/// <param name="Key">Unique id (e.g. "app:chrome").</param>
/// <param name="Text">Letter(s) for <see cref="IconGlyph.Text"/> / <see cref="IconGlyph.CalendarPage"/>.</param>
/// <param name="Color">Background color 0xRRGGBB.</param>
internal sealed record IconSpec(string Key, string Text, uint Color, IconShape Shape, int Size, IconGlyph Glyph = IconGlyph.Text, uint Foreground = 0xFFFFFF)
{
    public string CacheKey => $"icon:{Key}:{Size}";
}

/// <summary>Paints letter / symbol icons: soft vertical gradient background, crisp white glyph.</summary>
internal static class IconPainter
{
    public static byte[] RenderPng(IconSpec spec) => PngRenderer.RenderPng(spec.Size, spec.Size, dc => Draw(dc, spec));

    private static void Draw(DrawingContext dc, IconSpec spec)
    {
        double s = spec.Size;
        var bounds = new Rect(0, 0, s, s);
        var background = Linear(new Point(0, 0), new Point(0, 1),
            (0, Lighten(spec.Color, 0.14), 1),
            (1, Darken(spec.Color, 0.10), 1));

        Geometry outline = spec.Shape == IconShape.Circle
            ? new EllipseGeometry(bounds)
            : new RectangleGeometry(bounds, s * 0.26, s * 0.26);
        dc.DrawGeometry(background, null, Frozen(outline));

        var fg = Solid(spec.Foreground);
        var c = new Point(s / 2, s / 2);
        switch (spec.Glyph)
        {
            case IconGlyph.ChatBubble:
                DrawChatBubble(dc, fg, s);
                break;
            case IconGlyph.Envelope:
                DrawEnvelope(dc, fg, Solid(spec.Color), s);
                break;
            case IconGlyph.Play:
                dc.DrawRoundedRectangle(fg, null, new Rect(s * 0.20, s * 0.30, s * 0.60, s * 0.42), s * 0.12, s * 0.12);
                dc.DrawGeometry(Solid(spec.Color), null, Polygon([new(s * 0.44, s * 0.40), new(s * 0.60, s * 0.51), new(s * 0.44, s * 0.62)]));
                break;
            case IconGlyph.CalendarPage:
                dc.DrawRoundedRectangle(fg, null, new Rect(s * 0.24, s * 0.24, s * 0.52, s * 0.54), s * 0.08, s * 0.08);
                dc.DrawRectangle(Solid(Darken(spec.Color, 0.15)), null, new Rect(s * 0.24, s * 0.24, s * 0.52, s * 0.13));
                dc.DrawGeometry(Solid(spec.Color), null, CenteredText(spec.Text, "Segoe UI", FontWeights.Bold, s * 0.26, new Point(c.X, s * 0.585)));
                break;
            case IconGlyph.Cloud:
                dc.DrawEllipse(Solid(0xFFD54F), null, new Point(s * 0.40, s * 0.40), s * 0.15, s * 0.15);
                dc.DrawGeometry(fg, null, Frozen(new CombinedGeometry(GeometryCombineMode.Union,
                    new EllipseGeometry(new Point(s * 0.52, s * 0.54), s * 0.17, s * 0.15),
                    new RectangleGeometry(new Rect(s * 0.26, s * 0.54, s * 0.48, s * 0.14), s * 0.07, s * 0.07))));
                break;
            case IconGlyph.Bag:
                dc.DrawRoundedRectangle(fg, null, new Rect(s * 0.26, s * 0.36, s * 0.48, s * 0.38), s * 0.06, s * 0.06);
                dc.DrawGeometry(null, Frozen(new Pen(fg, s * 0.05)), Frozen(new EllipseGeometry(new Point(c.X, s * 0.36), s * 0.11, s * 0.11)));
                break;
            default:
                var em = spec.Text.Length switch { <= 1 => s * 0.50, 2 => s * 0.40, _ => s * 0.30 };
                dc.DrawGeometry(fg, null, CenteredText(spec.Text, "Segoe UI", FontWeights.SemiBold, em, c));
                break;
        }
    }

    private static void DrawChatBubble(DrawingContext dc, Brush fg, double s)
    {
        dc.DrawRoundedRectangle(fg, null, new Rect(s * 0.23, s * 0.27, s * 0.54, s * 0.38), s * 0.15, s * 0.15);
        dc.DrawGeometry(fg, null, Polygon([new(s * 0.31, s * 0.58), new(s * 0.29, s * 0.75), new(s * 0.47, s * 0.63)]));
    }

    private static void DrawEnvelope(DrawingContext dc, Brush fg, Brush accent, double s)
    {
        var body = new Rect(s * 0.22, s * 0.32, s * 0.56, s * 0.38);
        dc.DrawRoundedRectangle(fg, null, body, s * 0.05, s * 0.05);
        var pen = Frozen(new Pen(accent, s * 0.055) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var flap = new StreamGeometry();
        using (var ctx = flap.Open())
        {
            ctx.BeginFigure(new Point(body.Left + s * 0.05, body.Top + s * 0.06), false, false);
            ctx.LineTo(new Point(s * 0.5, body.Top + s * 0.22), true, true);
            ctx.LineTo(new Point(body.Right - s * 0.05, body.Top + s * 0.06), true, true);
        }
        dc.DrawGeometry(null, pen, Frozen(flap));
    }

    public static uint Lighten(uint rgb, double amount) => Mix(rgb, 0xFFFFFF, amount);

    public static uint Darken(uint rgb, double amount) => Mix(rgb, 0x000000, amount);

    private static uint Mix(uint a, uint b, double t)
    {
        static uint Channel(uint x, int shift) => (x >> shift) & 0xFF;
        uint Lerp(int shift) => (uint)Math.Round(Channel(a, shift) + (Channel(b, shift) - (double)Channel(a, shift)) * t);
        return (Lerp(16) << 16) | (Lerp(8) << 8) | Lerp(0);
    }
}
