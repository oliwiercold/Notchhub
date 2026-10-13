namespace NotchHub.Demo.Infrastructure;

/// <summary>
/// Mean-reverting random walk (discrete Ornstein–Uhlenbeck process): produces smooth, plausible sensor-like values
/// that wander around <see cref="Mean"/> inside [<see cref="Min"/>, <see cref="Max"/>]. Not thread-safe; callers lock.
/// </summary>
internal sealed class Jitter(double mean, double min, double max, double sigma, double reversion = 0.25)
{
    public double Mean { get; } = mean;
    public double Min { get; } = min;
    public double Max { get; } = max;
    public double Value { get; private set; } = mean;

    /// <summary>Advances one step and returns the new value.</summary>
    public double Next(Random rng)
    {
        var next = Value + (Mean - Value) * reversion + Gaussian(rng) * sigma;
        Value = Math.Clamp(next, Min, Max);
        return Value;
    }

    /// <summary>Standard normal sample (Box–Muller).</summary>
    public static double Gaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
