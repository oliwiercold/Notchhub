// Compact HCT (hue / chroma / tone) built on Cam16, after Google's material-color-utilities (Apache-2.0).
// Instead of the analytic HctSolver this uses a bisection on J (tone) and chroma (gamut) — plenty fast for scheme generation.
namespace NotchHub.UI.Theming.Mcu;

internal readonly record struct Hct(double Hue, double Chroma, double Tone, int Argb)
{
    public static Hct FromInt(int argb)
    {
        var cam = Cam16.FromInt(argb);
        return new Hct(cam.Hue, cam.Chroma, ColorUtils.LstarFromArgb(argb), argb);
    }

    public static Hct From(double hue, double chroma, double tone) => FromInt(Solve(hue, chroma, tone));

    /// <summary>ARGB of the color with the given hue/tone and the closest achievable chroma ≤ <paramref name="chroma"/>.</summary>
    public static int Solve(double hue, double chroma, double tone)
    {
        if (tone <= 0.01) return unchecked((int)0xFF000000);
        if (tone >= 99.99) return unchecked((int)0xFFFFFFFF);
        hue = MathUtils.SanitizeDegreesDouble(hue);
        if (chroma < 0.5) return ColorUtils.ArgbFromLstar(tone);
        if (TryAt(hue, chroma, tone, out var direct)) return direct;
        double lo = 0, hi = chroma;
        var best = ColorUtils.ArgbFromLstar(tone);
        for (var i = 0; i < 14; i++)
        {
            var mid = (lo + hi) / 2;
            if (TryAt(hue, mid, tone, out var argb)) { lo = mid; best = argb; }
            else hi = mid;
        }
        return best;
    }

    private static bool TryAt(double hue, double chroma, double tone, out int argb)
    {
        var targetY = ColorUtils.YFromLstar(tone);
        double lo = 0, hi = 100;
        double[] xyz = [0, 0, 0];
        for (var i = 0; i < 26; i++)
        {
            var j = (lo + hi) / 2;
            xyz = Cam16.FromJch(j, chroma, hue).XyzInViewingConditions(ViewingConditions.Default);
            if (xyz[1] < targetY) lo = j; else hi = j;
        }
        var m = ColorUtils.XyzToSrgb;
        double[] lin =
        [
            m[0][0] * xyz[0] + m[0][1] * xyz[1] + m[0][2] * xyz[2],
            m[1][0] * xyz[0] + m[1][1] * xyz[1] + m[1][2] * xyz[2],
            m[2][0] * xyz[0] + m[2][1] * xyz[1] + m[2][2] * xyz[2],
        ];
        argb = 0;
        foreach (var c in lin)
            if (double.IsNaN(c) || c < -0.05 || c > 100.05) return false;
        argb = ColorUtils.ArgbFromLinrgb(lin);
        return true;
    }

    /// <summary>Material "harmonize": rotates <paramref name="design"/>'s hue towards <paramref name="source"/> by up to 15°.</summary>
    public static Hct Harmonize(Hct design, Hct source, double tone, double chroma)
    {
        var diff = MathUtils.DifferenceDegrees(design.Hue, source.Hue);
        var rotation = Math.Min(diff * 0.5, 15.0);
        var hue = MathUtils.SanitizeDegreesDouble(design.Hue + rotation * MathUtils.RotationDirection(design.Hue, source.Hue));
        return From(hue, chroma, tone);
    }
}

/// <summary>Wallpaper color extraction: bucket quantizer + Material "Score" (ranking of suitable source colors).</summary>
internal static class ColorExtraction
{
    /// <summary>Quantizes ARGB pixels into 5-bit-per-channel buckets; returns averaged bucket color → population.</summary>
    public static Dictionary<int, int> Quantize(uint[] pixels)
    {
        var sums = new Dictionary<int, (long R, long G, long B, int N)>();
        foreach (var p in pixels)
        {
            if (p >> 24 < 255) continue;
            int r = (int)(p >> 16) & 255, g = (int)(p >> 8) & 255, b = (int)p & 255;
            var key = (r >> 3) << 10 | (g >> 3) << 5 | b >> 3;
            sums.TryGetValue(key, out var s);
            sums[key] = (s.R + r, s.G + g, s.B + b, s.N + 1);
        }
        var result = new Dictionary<int, int>(sums.Count);
        foreach (var s in sums.Values)
        {
            var argb = ColorUtils.ArgbFromRgb((int)(s.R / s.N), (int)(s.G / s.N), (int)(s.B / s.N));
            result[argb] = result.GetValueOrDefault(argb) + s.N;
        }
        return result;
    }

    /// <summary>Average in linear RGB ("as if blurred to one color").</summary>
    public static int Average(uint[] pixels)
    {
        double r = 0, g = 0, b = 0; long n = 0;
        foreach (var p in pixels)
        {
            if (p >> 24 < 255) continue;
            r += ColorUtils.Linearized((int)(p >> 16) & 255);
            g += ColorUtils.Linearized((int)(p >> 8) & 255);
            b += ColorUtils.Linearized((int)p & 255);
            n++;
        }
        return n == 0 ? unchecked((int)0xFF808080) : ColorUtils.ArgbFromLinrgb([r / n, g / n, b / n]);
    }

    /// <summary>Material Score: ranks colors by hue-excited population and chroma, de-duplicated by hue.</summary>
    public static List<int> Score(Dictionary<int, int> populations, int desired = 4, int fallback = unchecked((int)0xFF4285F4))
    {
        double total = 0;
        foreach (var n in populations.Values) total += n;
        var hueProportions = new double[360];
        var colors = new List<(Hct Hct, double Score)>(populations.Count);
        var hcts = new List<Hct>(populations.Count);
        foreach (var (argb, n) in populations)
        {
            var hct = Hct.FromInt(argb);
            hcts.Add(hct);
            hueProportions[(int)Math.Floor(hct.Hue) % 360] += n / Math.Max(1, total);
        }
        var excited = new double[360];
        for (var i = 0; i < 360; i++)
            for (var j = i - 14; j < i + 16; j++)
                excited[i] += hueProportions[MathUtils.SanitizeDegreesInt(j)];

        foreach (var hct in hcts)
        {
            var proportion = excited[(int)Math.Floor(hct.Hue) % 360];
            if (hct.Chroma < 5 || proportion <= 0.01) continue;
            var score = proportion * 100 * 0.7 + (hct.Chroma - 48) * (hct.Chroma < 48 ? 0.1 : 0.3);
            colors.Add((hct, score));
        }
        colors.Sort((a, b) => b.Score.CompareTo(a.Score));

        var chosen = new List<Hct>();
        for (var diff = 90; diff >= 15; diff--)
        {
            chosen.Clear();
            foreach (var (hct, _) in colors)
            {
                if (chosen.Exists(c => MathUtils.DifferenceDegrees(hct.Hue, c.Hue) < diff)) continue;
                chosen.Add(hct);
                if (chosen.Count >= desired) break;
            }
            if (chosen.Count >= desired) break;
        }
        var result = chosen.ConvertAll(c => c.Argb);
        if (result.Count == 0) result.Add(fallback);
        return result;
    }
}
