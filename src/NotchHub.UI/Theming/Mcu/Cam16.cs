// Port of Google's material-color-utilities (Apache-2.0), hct/Cam16 + hct/ViewingConditions.
namespace NotchHub.UI.Theming.Mcu;

/// <summary>Environment in which colors are viewed (CAM16). <see cref="Default"/> = sRGB, D65, 200 lux, average surround.</summary>
internal sealed class ViewingConditions
{
    public static readonly ViewingConditions Default = DefaultWithBackgroundLstar(50.0);

    private ViewingConditions(double n, double aw, double nbb, double ncb, double c, double nc, double[] rgbD, double fl, double flRoot, double z)
    {
        N = n; Aw = aw; Nbb = nbb; Ncb = ncb; C = c; Nc = nc; RgbD = rgbD; Fl = fl; FlRoot = flRoot; Z = z;
    }

    public double Aw { get; }
    public double N { get; }
    public double Nbb { get; }
    public double Ncb { get; }
    public double C { get; }
    public double Nc { get; }
    public double[] RgbD { get; }
    public double Fl { get; }
    public double FlRoot { get; }
    public double Z { get; }

    public static ViewingConditions Make(double[] whitePoint, double adaptingLuminance, double backgroundLstar, double surround, bool discountingIlluminant)
    {
        backgroundLstar = Math.Max(0.1, backgroundLstar);
        var m = Cam16.XyzToCam16Rgb;
        var xyz = whitePoint;
        var rW = xyz[0] * m[0][0] + xyz[1] * m[0][1] + xyz[2] * m[0][2];
        var gW = xyz[0] * m[1][0] + xyz[1] * m[1][1] + xyz[2] * m[1][2];
        var bW = xyz[0] * m[2][0] + xyz[1] * m[2][1] + xyz[2] * m[2][2];
        var f = 0.8 + surround / 10.0;
        var c = f >= 0.9 ? MathUtils.Lerp(0.59, 0.69, (f - 0.9) * 10.0) : MathUtils.Lerp(0.525, 0.59, (f - 0.8) * 10.0);
        var d = discountingIlluminant ? 1.0 : f * (1.0 - 1.0 / 3.6 * Math.Exp((-adaptingLuminance - 42.0) / 92.0));
        d = MathUtils.ClampDouble(0.0, 1.0, d);
        var nc = f;
        double[] rgbD = [d * (100.0 / rW) + 1.0 - d, d * (100.0 / gW) + 1.0 - d, d * (100.0 / bW) + 1.0 - d];
        var k = 1.0 / (5.0 * adaptingLuminance + 1.0);
        var k4 = k * k * k * k;
        var k4F = 1.0 - k4;
        var fl = k4 * adaptingLuminance + 0.1 * k4F * k4F * Math.Cbrt(5.0 * adaptingLuminance);
        var n = ColorUtils.YFromLstar(backgroundLstar) / whitePoint[1];
        var z = 1.48 + Math.Sqrt(n);
        var nbb = 0.725 / Math.Pow(n, 0.2);
        var ncb = nbb;
        double[] rgbAFactors =
        [
            Math.Pow(fl * rgbD[0] * rW / 100.0, 0.42),
            Math.Pow(fl * rgbD[1] * gW / 100.0, 0.42),
            Math.Pow(fl * rgbD[2] * bW / 100.0, 0.42),
        ];
        double[] rgbA =
        [
            400.0 * rgbAFactors[0] / (rgbAFactors[0] + 27.13),
            400.0 * rgbAFactors[1] / (rgbAFactors[1] + 27.13),
            400.0 * rgbAFactors[2] / (rgbAFactors[2] + 27.13),
        ];
        var aw = (2.0 * rgbA[0] + rgbA[1] + 0.05 * rgbA[2]) * nbb;
        return new ViewingConditions(n, aw, nbb, ncb, c, nc, rgbD, fl, Math.Pow(fl, 0.25), z);
    }

    public static ViewingConditions DefaultWithBackgroundLstar(double lstar) =>
        Make(ColorUtils.WhitePointD65, 200.0 / Math.PI * ColorUtils.YFromLstar(50.0) / 100.0, lstar, 2.0, false);
}

/// <summary>CAM16 color appearance model: hue, chroma and lightness (J) as perceived in given viewing conditions.</summary>
internal sealed class Cam16
{
    internal static readonly double[][] XyzToCam16Rgb =
    [
        [0.401288, 0.650173, -0.051461],
        [-0.250268, 1.204414, 0.045854],
        [-0.002079, 0.048952, 0.953127],
    ];

    internal static readonly double[][] Cam16RgbToXyz =
    [
        [1.8620678, -1.0112547, 0.14918678],
        [0.38752654, 0.62144744, -0.00897398],
        [-0.01584150, -0.03412294, 1.0499644],
    ];

    private Cam16(double hue, double chroma, double j, double q, double m, double s, double jstar, double astar, double bstar)
    {
        Hue = hue; Chroma = chroma; J = j; Q = q; M = m; S = s; Jstar = jstar; Astar = astar; Bstar = bstar;
    }

    public double Hue { get; }
    public double Chroma { get; }
    public double J { get; }
    public double Q { get; }
    public double M { get; }
    public double S { get; }
    public double Jstar { get; }
    public double Astar { get; }
    public double Bstar { get; }

    /// <summary>CAM16-UCS color difference.</summary>
    public double Distance(Cam16 other)
    {
        var dJ = Jstar - other.Jstar;
        var dA = Astar - other.Astar;
        var dB = Bstar - other.Bstar;
        return 1.41 * Math.Pow(Math.Sqrt(dJ * dJ + dA * dA + dB * dB), 0.63);
    }

    public static Cam16 FromInt(int argb) => FromIntInViewingConditions(argb, ViewingConditions.Default);

    public static Cam16 FromIntInViewingConditions(int argb, ViewingConditions vc)
    {
        var redL = ColorUtils.Linearized((argb & 0x00ff0000) >> 16);
        var greenL = ColorUtils.Linearized((argb & 0x0000ff00) >> 8);
        var blueL = ColorUtils.Linearized(argb & 0x000000ff);
        var x = 0.41233895 * redL + 0.35762064 * greenL + 0.18051042 * blueL;
        var y = 0.2126 * redL + 0.7152 * greenL + 0.0722 * blueL;
        var z = 0.01932141 * redL + 0.11916382 * greenL + 0.95034478 * blueL;
        return FromXyzInViewingConditions(x, y, z, vc);
    }

    public static Cam16 FromXyzInViewingConditions(double x, double y, double z, ViewingConditions vc)
    {
        var m = XyzToCam16Rgb;
        var rT = x * m[0][0] + y * m[0][1] + z * m[0][2];
        var gT = x * m[1][0] + y * m[1][1] + z * m[1][2];
        var bT = x * m[2][0] + y * m[2][1] + z * m[2][2];

        var rD = vc.RgbD[0] * rT;
        var gD = vc.RgbD[1] * gT;
        var bD = vc.RgbD[2] * bT;

        var rAF = Math.Pow(vc.Fl * Math.Abs(rD) / 100.0, 0.42);
        var gAF = Math.Pow(vc.Fl * Math.Abs(gD) / 100.0, 0.42);
        var bAF = Math.Pow(vc.Fl * Math.Abs(bD) / 100.0, 0.42);
        var rA = Math.Sign(rD) * 400.0 * rAF / (rAF + 27.13);
        var gA = Math.Sign(gD) * 400.0 * gAF / (gAF + 27.13);
        var bA = Math.Sign(bD) * 400.0 * bAF / (bAF + 27.13);

        var a = (11.0 * rA + -12.0 * gA + bA) / 11.0;
        var b = (rA + gA - 2.0 * bA) / 9.0;
        var u = (20.0 * rA + 20.0 * gA + 21.0 * bA) / 20.0;
        var p2 = (40.0 * rA + 20.0 * gA + bA) / 20.0;

        var atanDegrees = Math.Atan2(b, a) * 180.0 / Math.PI;
        var hue = MathUtils.SanitizeDegreesDouble(atanDegrees);
        var hueRadians = hue * Math.PI / 180.0;

        var ac = p2 * vc.Nbb;
        var j = 100.0 * Math.Pow(ac / vc.Aw, vc.C * vc.Z);
        var q = 4.0 / vc.C * Math.Sqrt(j / 100.0) * (vc.Aw + 4.0) * vc.FlRoot;

        var huePrime = hue < 20.14 ? hue + 360 : hue;
        var eHue = 0.25 * (Math.Cos(huePrime * Math.PI / 180.0 + 2.0) + 3.8);
        var p1 = 50000.0 / 13.0 * eHue * vc.Nc * vc.Ncb;
        var t = p1 * Math.Sqrt(a * a + b * b) / (u + 0.305);
        var alpha = Math.Pow(1.64 - Math.Pow(0.29, vc.N), 0.73) * Math.Pow(t, 0.9);
        var c = alpha * Math.Sqrt(j / 100.0);
        var mm = c * vc.FlRoot;
        var s = 50.0 * Math.Sqrt(alpha * vc.C / (vc.Aw + 4.0));

        var jstar = (1.0 + 100.0 * 0.007) * j / (1.0 + 0.007 * j);
        var mstar = 1.0 / 0.0228 * Math.Log(1 + 0.0228 * mm);
        var astar = mstar * Math.Cos(hueRadians);
        var bstar = mstar * Math.Sin(hueRadians);
        return new Cam16(hue, c, j, q, mm, s, jstar, astar, bstar);
    }

    public static Cam16 FromJch(double j, double c, double h) => FromJchInViewingConditions(j, c, h, ViewingConditions.Default);

    private static Cam16 FromJchInViewingConditions(double j, double c, double h, ViewingConditions vc)
    {
        var q = 4.0 / vc.C * Math.Sqrt(j / 100.0) * (vc.Aw + 4.0) * vc.FlRoot;
        var m = c * vc.FlRoot;
        var alpha = c / Math.Sqrt(j / 100.0);
        var s = 50.0 * Math.Sqrt(alpha * vc.C / (vc.Aw + 4.0));
        var hueRadians = h * Math.PI / 180.0;
        var jstar = (1.0 + 100.0 * 0.007) * j / (1.0 + 0.007 * j);
        var mstar = 1.0 / 0.0228 * Math.Log(1 + 0.0228 * m);
        return new Cam16(h, c, j, q, m, s, jstar, mstar * Math.Cos(hueRadians), mstar * Math.Sin(hueRadians));
    }

    public static Cam16 FromUcs(double jstar, double astar, double bstar)
    {
        var vc = ViewingConditions.Default;
        var m = Math.Sqrt(astar * astar + bstar * bstar);
        var m2 = (Math.Exp(m * 0.0228) - 1.0) / 0.0228;
        var c = m2 / vc.FlRoot;
        var h = Math.Atan2(bstar, astar) * (180.0 / Math.PI);
        if (h < 0.0) h += 360.0;
        var j = jstar / (1.0 - (jstar - 100.0) * 0.007);
        return FromJchInViewingConditions(j, c, h, vc);
    }

    public int ToInt() => Viewed(ViewingConditions.Default);

    public int Viewed(ViewingConditions vc)
    {
        var xyz = XyzInViewingConditions(vc);
        return ColorUtils.ArgbFromXyz(xyz[0], xyz[1], xyz[2]);
    }

    public double[] XyzInViewingConditions(ViewingConditions vc)
    {
        var alpha = Chroma == 0.0 || J == 0.0 ? 0.0 : Chroma / Math.Sqrt(J / 100.0);
        var t = Math.Pow(alpha / Math.Pow(1.64 - Math.Pow(0.29, vc.N), 0.73), 1.0 / 0.9);
        var hRad = Hue * Math.PI / 180.0;
        var eHue = 0.25 * (Math.Cos(hRad + 2.0) + 3.8);
        var ac = vc.Aw * Math.Pow(J / 100.0, 1.0 / vc.C / vc.Z);
        var p1 = eHue * (50000.0 / 13.0) * vc.Nc * vc.Ncb;
        var p2 = ac / vc.Nbb;
        var hSin = Math.Sin(hRad);
        var hCos = Math.Cos(hRad);
        var gamma = 23.0 * (p2 + 0.305) * t / (23.0 * p1 + 11.0 * t * hCos + 108.0 * t * hSin);
        var a = gamma * hCos;
        var b = gamma * hSin;
        var rA = (460.0 * p2 + 451.0 * a + 288.0 * b) / 1403.0;
        var gA = (460.0 * p2 - 891.0 * a - 261.0 * b) / 1403.0;
        var bA = (460.0 * p2 - 220.0 * a - 6300.0 * b) / 1403.0;

        var rCBase = Math.Max(0, 27.13 * Math.Abs(rA) / (400.0 - Math.Abs(rA)));
        var rC = Math.Sign(rA) * (100.0 / vc.Fl) * Math.Pow(rCBase, 1.0 / 0.42);
        var gCBase = Math.Max(0, 27.13 * Math.Abs(gA) / (400.0 - Math.Abs(gA)));
        var gC = Math.Sign(gA) * (100.0 / vc.Fl) * Math.Pow(gCBase, 1.0 / 0.42);
        var bCBase = Math.Max(0, 27.13 * Math.Abs(bA) / (400.0 - Math.Abs(bA)));
        var bC = Math.Sign(bA) * (100.0 / vc.Fl) * Math.Pow(bCBase, 1.0 / 0.42);
        var rF = rC / vc.RgbD[0];
        var gF = gC / vc.RgbD[1];
        var bF = bC / vc.RgbD[2];

        var m = Cam16RgbToXyz;
        return
        [
            rF * m[0][0] + gF * m[0][1] + bF * m[0][2],
            rF * m[1][0] + gF * m[1][1] + bF * m[1][2],
            rF * m[2][0] + gF * m[2][1] + bF * m[2][2],
        ];
    }
}
