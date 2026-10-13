using System.Runtime.InteropServices;

namespace NotchHub.App.Interop;

/// <summary>Reads a few screen pixels (composited desktop, physical coordinates) to estimate how dark the area around the notch is.</summary>
internal static class ScreenSampler
{
    /// <summary>Average relative luminance 0..1 of the given points, or null when sampling failed. Safe to call off the UI thread.</summary>
    public static double? AverageLuminance(IReadOnlyList<(int X, int Y)> points)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        try
        {
            double sum = 0;
            var n = 0;
            foreach (var (x, y) in points)
            {
                var c = GetPixel(dc, x, y);
                if (c == CLR_INVALID) continue;
                double r = (c & 0xFF) / 255.0, g = ((c >> 8) & 0xFF) / 255.0, b = ((c >> 16) & 0xFF) / 255.0;
                sum += 0.2126 * r + 0.7152 * g + 0.0722 * b;
                n++;
            }
            return n == 0 ? null : sum / n;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private const uint CLR_INVALID = 0xFFFFFFFF;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hdc, int x, int y);
}
