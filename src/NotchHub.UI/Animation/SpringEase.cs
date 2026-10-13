using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NotchHub.UI.Animation;

/// <summary>
/// Physical spring (mass 1) as a WPF easing function. The animation duration must be <see cref="Duration"/>
/// (the settle time), use <see cref="Motion.To(double, SpringEase)"/> or <see cref="Motion.Animate"/> to get it right.
/// Under-damped springs overshoot — that is the M3 Expressive "spatial" look.
/// </summary>
public sealed class SpringEase : IEasingFunction
{
    private readonly double _omega;   // natural frequency
    private readonly double _omegaD;  // damped frequency

    public SpringEase(double dampingRatio, double stiffness)
    {
        DampingRatio = Math.Clamp(dampingRatio, 0.05, 1.0);
        Stiffness = Math.Max(1, stiffness);
        _omega = Math.Sqrt(Stiffness);
        _omegaD = DampingRatio < 1 ? _omega * Math.Sqrt(1 - DampingRatio * DampingRatio) : 0;
        Duration = TimeSpan.FromSeconds(SettleTime());
    }

    public double DampingRatio { get; }
    public double Stiffness { get; }

    /// <summary>Time until the motion is within 0.1 % of the target.</summary>
    public TimeSpan Duration { get; }

    public double Ease(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        if (normalizedTime >= 1) return 1;
        return Position(normalizedTime * Duration.TotalSeconds);
    }

    private double Position(double t)
    {
        if (DampingRatio >= 1)
            return 1 - Math.Exp(-_omega * t) * (1 + _omega * t);
        var envelope = Math.Exp(-DampingRatio * _omega * t);
        return 1 - envelope * (Math.Cos(_omegaD * t) + DampingRatio * _omega / _omegaD * Math.Sin(_omegaD * t));
    }

    private double SettleTime()
    {
        // walk forward until the response stays within the tolerance band
        const double tolerance = 0.001, step = 0.004;
        double lastOutside = 0;
        for (double t = 0; t < 3; t += step)
            if (Math.Abs(1 - Position(t)) > tolerance) lastOutside = t;
        return Math.Max(0.05, lastOutside + step);
    }
}

/// <summary>M3 Expressive motion tokens (see docs/DESIGN.md §4).</summary>
public static class Springs
{
    public static SpringEase SpatialFast { get; } = new(0.6, 800);
    public static SpringEase SpatialDefault { get; } = new(0.8, 380);
    public static SpringEase SpatialSlow { get; } = new(0.8, 200);
    public static SpringEase EffectsFast { get; } = new(1.0, 3800);
    public static SpringEase EffectsDefault { get; } = new(1.0, 1600);
    public static SpringEase EffectsSlow { get; } = new(1.0, 800);
}

/// <summary>Animation helpers that honor <see cref="ReduceMotion"/>.</summary>
public static partial class Motion
{
    /// <summary>Global switch (bound to settings Appearance.ReduceMotion, forced on in screenshot mode).</summary>
    public static bool ReduceMotion { get; set; }

    public static Duration DurationOf(SpringEase spring) =>
        new(ReduceMotion ? TimeSpan.FromMilliseconds(1) : spring.Duration);

    public static DoubleAnimation To(double to, SpringEase spring) =>
        new(to, DurationOf(spring)) { EasingFunction = spring };

    /// <summary>Animates a double DP from its current (possibly animated) value to <paramref name="to"/>.</summary>
    public static void Animate(this IAnimatable target, DependencyProperty property, double to, SpringEase spring, Action? completed = null)
    {
        if (target is DependencyObject d) StopChannel(d, property);
        var anim = To(to, spring);
        var entry = TrackLegacy(target, property, to, completed);
        anim.Completed += (_, _) =>
        {
            if (!UntrackLegacy(entry)) return;      // already landed by SnapAll
            completed?.Invoke();
        };
        target.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
    }

    public static void AnimateColor(this IAnimatable target, DependencyProperty property, Color to, SpringEase spring)
    {
        var anim = new ColorAnimation(to, DurationOf(spring)) { EasingFunction = spring };
        target.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
    }

    public static void AnimateThickness(this IAnimatable target, DependencyProperty property, Thickness to, SpringEase spring)
    {
        var anim = new ThicknessAnimation(to, DurationOf(spring)) { EasingFunction = spring };
        target.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
    }
}
