// Port of Google's material-color-utilities (Apache-2.0), utils/ColorUtils + utils/MathUtils.
// https://github.com/material-foundation/material-color-utilities
namespace NotchHub.UI.Theming.Mcu;

/// <summary>Color science helpers: sRGB ⇄ linear RGB ⇄ XYZ ⇄ L*a*b*. ARGB colors are packed as 0xAARRGGBB in an int.</summary>
internal static class ColorUtils
{
    internal static readonly double[][] SrgbToXyz =
    [
        [0.41233895, 0.35762064, 0.18051042],
        [0.2126, 0.7152, 0.0722],
        [0.01932141, 0.11916382, 0.95034478],
    ];

    internal static readonly double[][] XyzToSrgb =
    [
        [3.2413774792388685, -1.5376652402851851, -0.49885366846268053],
        [-0.9691452513005321, 1.8758853451067872, 0.04156585616912061],
        [0.05562093689691305, -0.20395524564742123, 1.0571799111220335],
    ];

    internal static readonly double[] WhitePointD65 = [95.047, 100.0, 108.883];

    public static int ArgbFromRgb(int red, int green, int blue) =>
        (255 << 24) | ((red & 255) << 16) | ((green & 255) << 8) | (blue & 255);

    public static int ArgbFromLinrgb(double[] linrgb) =>
        ArgbFromRgb(Delinearized(linrgb[0]), Delinearized(linrgb[1]), Delinearized(linrgb[2]));

    public static int AlphaFromArgb(int argb) => (argb >> 24) & 255;
    public static int RedFromArgb(int argb) => (argb >> 16) & 255;
    public static int GreenFromArgb(int argb) => (argb >> 8) & 255;
    public static int BlueFromArgb(int argb) => argb & 255;
    public static bool IsOpaque(int argb) => AlphaFromArgb(argb) >= 255;

    public static int ArgbFromXyz(double x, double y, double z)
    {
        var m = XyzToSrgb;
        var linearR = m[0][0] * x + m[0][1] * y + m[0][2] * z;
        var linearG = m[1][0] * x + m[1][1] * y + m[1][2] * z;
        var linearB = m[2][0] * x + m[2][1] * y + m[2][2] * z;
        return ArgbFromRgb(Delinearized(linearR), Delinearized(linearG), Delinearized(linearB));
    }

    public static double[] XyzFromArgb(int argb)
    {
        var r = Linearized(RedFromArgb(argb));
        var g = Linearized(GreenFromArgb(argb));
        var b = Linearized(BlueFromArgb(argb));
        return MathUtils.MatrixMultiply([r, g, b], SrgbToXyz);
    }

    public static int ArgbFromLab(double l, double a, double b)
    {
        var wp = WhitePointD65;
        var fy = (l + 16.0) / 116.0;
        var fx = a / 500.0 + fy;
        var fz = fy - b / 200.0;
        return ArgbFromXyz(LabInvf(fx) * wp[0], LabInvf(fy) * wp[1], LabInvf(fz) * wp[2]);
    }

    public static double[] LabFromArgb(int argb)
    {
        var linearR = Linearized(RedFromArgb(argb));
        var linearG = Linearized(GreenFromArgb(argb));
        var linearB = Linearized(BlueFromArgb(argb));
        var m = SrgbToXyz;
        var x = m[0][0] * linearR + m[0][1] * linearG + m[0][2] * linearB;
        var y = m[1][0] * linearR + m[1][1] * linearG + m[1][2] * linearB;
        var z = m[2][0] * linearR + m[2][1] * linearG + m[2][2] * linearB;
        var wp = WhitePointD65;
        var fx = LabF(x / wp[0]);
        var fy = LabF(y / wp[1]);
        var fz = LabF(z / wp[2]);
        return [116.0 * fy - 16, 500.0 * (fx - fy), 200.0 * (fy - fz)];
    }

    public static int ArgbFromLstar(double lstar)
    {
        var component = Delinearized(YFromLstar(lstar));
        return ArgbFromRgb(component, component, component);
    }

    public static double LstarFromArgb(int argb) => 116.0 * LabF(XyzFromArgb(argb)[1] / 100.0) - 16.0;

    public static double YFromLstar(double lstar) => 100.0 * LabInvf((lstar + 16.0) / 116.0);

    public static double LstarFromY(double y) => LabF(y / 100.0) * 116.0 - 16.0;

    /// <summary>sRGB component 0..255 → linear RGB 0..100.</summary>
    public static double Linearized(int rgbComponent)
    {
        var normalized = rgbComponent / 255.0;
        return normalized <= 0.040449936
            ? normalized / 12.92 * 100.0
            : Math.Pow((normalized + 0.055) / 1.055, 2.4) * 100.0;
    }

    /// <summary>Linear RGB 0..100 → sRGB component 0..255.</summary>
    public static int Delinearized(double rgbComponent)
    {
        var normalized = rgbComponent / 100.0;
        var delinearized = normalized <= 0.0031308
            ? normalized * 12.92
            : 1.055 * Math.Pow(normalized, 1.0 / 2.4) - 0.055;
        return MathUtils.ClampInt(0, 255, MathUtils.Round(delinearized * 255.0));
    }

    private static double LabF(double t)
    {
        const double e = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        return t > e ? Math.Pow(t, 1.0 / 3.0) : (kappa * t + 16) / 116;
    }

    private static double LabInvf(double ft)
    {
        const double e = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        var ft3 = ft * ft * ft;
        return ft3 > e ? ft3 : (116 * ft - 16) / kappa;
    }
}

/// <summary>Numeric helpers shared by the color algorithms.</summary>
internal static class MathUtils
{
    public static int Signum(double num) => num < 0 ? -1 : num == 0 ? 0 : 1;

    /// <summary>Java's Math.round semantics (floor(x + 0.5)), used throughout the reference implementation.</summary>
    public static int Round(double x) => (int)Math.Floor(x + 0.5);

    public static double Lerp(double start, double stop, double amount) => (1.0 - amount) * start + amount * stop;

    public static int ClampInt(int min, int max, int input) => input < min ? min : input > max ? max : input;

    public static double ClampDouble(double min, double max, double input) => input < min ? min : input > max ? max : input;

    public static int SanitizeDegreesInt(int degrees)
    {
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    public static double SanitizeDegreesDouble(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    public static double RotationDirection(double from, double to) =>
        SanitizeDegreesDouble(to - from) <= 180.0 ? 1.0 : -1.0;

    public static double DifferenceDegrees(double a, double b) => 180.0 - Math.Abs(Math.Abs(a - b) - 180.0);

    public static double[] MatrixMultiply(double[] row, double[][] matrix) =>
    [
        row[0] * matrix[0][0] + row[1] * matrix[0][1] + row[2] * matrix[0][2],
        row[0] * matrix[1][0] + row[1] * matrix[1][1] + row[2] * matrix[1][2],
        row[0] * matrix[2][0] + row[1] * matrix[2][1] + row[2] * matrix[2][2],
    ];
}
