using System.Windows;
using System.Windows.Media;
using static NotchHub.Demo.Imaging.PngRenderer;

namespace NotchHub.Demo.Imaging;

/// <summary>Paints a believable "screenshot" of a dark code editor (abstract: syntax-colored bars instead of text).</summary>
internal static class ScreenshotPainter
{
    private static readonly uint[] Syntax = [0x569CD6, 0x4EC9B0, 0xCE9178, 0xDCDCAA, 0x9CDCFE, 0xC586C0, 0xD4D4D4, 0xD4D4D4, 0x9CDCFE];

    private static SolidColorBrush Solid(int rgb, double alpha = 1.0) => PngRenderer.Solid((uint)rgb, alpha);
    private static SolidColorBrush Solid(uint rgb, double alpha = 1.0) => PngRenderer.Solid(rgb, alpha);

    public static void Draw(DrawingContext dc, double width, double height)
    {
        var rng = new Random(1080);
        var u = height / 1080.0; // layout unit

        dc.DrawRectangle(Solid(0x1E1E1E), null, new Rect(0, 0, width, height));

        // Title bar, activity bar, side bar, tabs, status bar.
        var title = 34 * u;
        var status = 26 * u;
        var activity = 52 * u;
        var sidebar = 330 * u;
        dc.DrawRectangle(Solid(0x2D2D30), null, new Rect(0, 0, width, title));
        foreach (var (x, c) in new[] { (16.0, 0xFF5F57u), (38.0, 0xFEBC2Eu), (60.0, 0x28C840u) })
            dc.DrawEllipse(Solid(c), null, new Point(x * u, title / 2), 6 * u, 6 * u);
        dc.DrawRoundedRectangle(Solid(0x3C3C3C), null, new Rect(width / 2 - 260 * u, 6 * u, 520 * u, 22 * u), 6 * u, 6 * u);

        dc.DrawRectangle(Solid(0x333333), null, new Rect(0, title, activity, height - title - status));
        for (var i = 0; i < 5; i++)
            dc.DrawRoundedRectangle(Solid(i == 0 ? 0xD7D7D7 : 0x858585), null, new Rect(14 * u, title + (18 + i * 52) * u, 24 * u, 24 * u), 5 * u, 5 * u);

        dc.DrawRectangle(Solid(0x252526), null, new Rect(activity, title, sidebar, height - title - status));
        var y = title + 46 * u;
        for (var i = 0; i < 26; i++)
        {
            var depth = i == 0 ? 0 : 1 + rng.Next(3);
            var w = (70 + rng.Next(150)) * u;
            var selected = i == 7;
            if (selected) dc.DrawRectangle(Solid(0x37373D), null, new Rect(activity, y - 6 * u, sidebar, 26 * u));
            dc.DrawRoundedRectangle(Solid(0xC5A35A, 0.9), null, new Rect(activity + (20 + depth * 16) * u, y, 12 * u, 12 * u), 2 * u, 2 * u);
            dc.DrawRoundedRectangle(Solid(selected ? 0xFFFFFF : 0xCCCCCC, 0.75), null, new Rect(activity + (40 + depth * 16) * u, y + 2 * u, w, 9 * u), 4 * u, 4 * u);
            y += 30 * u;
        }

        var editorLeft = activity + sidebar;
        var tabs = 40 * u;
        dc.DrawRectangle(Solid(0x252526), null, new Rect(editorLeft, title, width - editorLeft, tabs));
        for (var i = 0; i < 4; i++)
        {
            var tab = new Rect(editorLeft + i * 190 * u, title, 190 * u, tabs);
            dc.DrawRectangle(Solid(i == 0 ? 0x1E1E1E : 0x2D2D2D), null, tab);
            dc.DrawRoundedRectangle(Solid(0xCCCCCC, i == 0 ? 0.85 : 0.45), null, new Rect(tab.X + 40 * u, tab.Y + 16 * u, 110 * u, 9 * u), 4 * u, 4 * u);
            dc.DrawRoundedRectangle(Solid(0x519ABA), null, new Rect(tab.X + 16 * u, tab.Y + 14 * u, 14 * u, 13 * u), 3 * u, 3 * u);
        }

        // Code: line numbers + syntax-colored tokens with indentation.
        var terminalTop = height - status - 260 * u;
        y = title + tabs + 18 * u;
        var indent = 0;
        var line = 1;
        while (y < terminalTop - 24 * u)
        {
            dc.DrawRoundedRectangle(Solid(0x858585, 0.6), null, new Rect(editorLeft + 18 * u, y + 3 * u, (line < 10 ? 10 : 20) * u, 10 * u), 3 * u, 3 * u);
            if (rng.NextDouble() > 0.12)
            {
                var x = editorLeft + (80 + indent * 32) * u;
                var tokens = 1 + rng.Next(5);
                for (var t = 0; t < tokens && x < width - 320 * u; t++)
                {
                    var w = (24 + rng.Next(120)) * u;
                    var color = t == 0 && rng.NextDouble() < 0.15 ? 0x6A9955u : Syntax[rng.Next(Syntax.Length)];
                    dc.DrawRoundedRectangle(Solid(color, 0.9), null, new Rect(x, y + 3 * u, w, 11 * u), 4 * u, 4 * u);
                    x += w + 10 * u;
                }
                indent = Math.Clamp(indent + rng.Next(-1, 2), 0, 4);
            }
            y += 25 * u;
            line++;
        }

        // Minimap.
        for (var my = title + tabs + 10 * u; my < terminalTop - 10 * u; my += 5 * u)
            dc.DrawRectangle(Solid(Syntax[rng.Next(Syntax.Length)], 0.35), null, new Rect(width - 120 * u, my, (20 + rng.Next(80)) * u, 2.5 * u));

        // Terminal panel.
        dc.DrawRectangle(Solid(0x181818), null, new Rect(editorLeft, terminalTop, width - editorLeft, height - status - terminalTop));
        dc.DrawRectangle(Solid(0x2B2B2B), null, new Rect(editorLeft, terminalTop, width - editorLeft, 1.5 * u));
        y = terminalTop + 46 * u;
        for (var i = 0; i < 6; i++)
        {
            var prompt = i % 3 == 0;
            dc.DrawRoundedRectangle(Solid(prompt ? 0x4EC9B0 : 0xCCCCCC, prompt ? 0.9 : 0.55), null,
                new Rect(editorLeft + 24 * u, y, (prompt ? 360 : 200 + rng.Next(500)) * u, 10 * u), 4 * u, 4 * u);
            y += 28 * u;
        }

        dc.DrawRectangle(Solid(0x007ACC), null, new Rect(0, height - status, width, status));
        dc.DrawRoundedRectangle(Solid(0xFFFFFF, 0.8), null, new Rect(14 * u, height - status + 8 * u, 120 * u, 10 * u), 4 * u, 4 * u);
        dc.DrawRoundedRectangle(Solid(0xFFFFFF, 0.8), null, new Rect(width - 300 * u, height - status + 8 * u, 260 * u, 10 * u), 4 * u, 4 * u);
    }
}
