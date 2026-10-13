using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Animation;

namespace NotchHub.UI.Animation;

/// <summary>
/// Legacy = SpringEase storyboards (velocity lost on interruption); Refreshed = interruptible physics springs;
/// Smooth = Refreshed + velocity-proportional motion blur on moving content (best on high refresh-rate monitors).
/// </summary>
public enum MotionProfile { Legacy, Refreshed, Smooth }

/// <summary>
/// Motion tokens (docs/MOTION.md §4). The first six are the M3 Expressive scheme; the rest are the Refreshed shell set
/// (they map to the nearest M3 token under Legacy).
/// </summary>
public enum MotionToken
{
    SpatialFast, SpatialDefault, SpatialSlow, EffectsFast, EffectsDefault, EffectsSlow,
    /// <summary>Panel height when it grows (0.40 / 0.80).</summary>
    Grow,
    /// <summary>Panel width when it grows — leads height (0.36 / 0.78).</summary>
    GrowWide,
    /// <summary>Height when it shrinks — firm (0.36 / 0.92).</summary>
    Shrink,
    /// <summary>Width when it shrinks — trails height (0.40 / 0.90).</summary>
    ShrinkWide,
    /// <summary>HUD, live activity and peek size (0.30 / 0.75).</summary>
    Small,
    /// <summary>Content scale / translate in (0.32 / 0.86).</summary>
    ContentIn,
    /// <summary>Small icons and art only (0.28 / 0.62).</summary>
    Pop,
    /// <summary>Press down, no bounce (0.12 / 1).</summary>
    Press,
    /// <summary>Press release (0.26 / 0.62).</summary>
    Release,
    /// <summary>Values that represent data — never overshoot (0.24 / 1).</summary>
    Data,
    /// <summary>Opacity / blur / color (0.10 / 1).</summary>
    FxFast,
    /// <summary>Opacity / blur / color (0.16 / 1).</summary>
    Fx,
    /// <summary>Opacity / blur / color (0.22 / 1).</summary>
    FxSlow,
}

public static partial class Motion
{
    private static MotionProfile _profile = MotionProfile.Refreshed;

    /// <summary>Current motion profile. Changing it lands every running animation on its target, then raises <see cref="ProfileChanged"/>.</summary>
    public static MotionProfile Profile
    {
        get => _profile;
        set
        {
            if (_profile == value) return;
            SnapAll();
            _profile = value;
            ProfileChanged?.Invoke();
        }
    }

    /// <summary>Physics springs (Refreshed or Smooth).</summary>
    public static bool IsRefreshed => _profile != MotionProfile.Legacy;

    /// <summary>Smooth profile: motion blur on fast movement (only when the GPU renders, see <see cref="CanBlur"/>).</summary>
    public static bool IsSmooth => _profile == MotionProfile.Smooth && !ReduceMotion && CanBlur;

    /// <summary>Hardware rendering tier 2 (pixel-shader effects are cheap); false under software rendering.</summary>
    public static bool CanBlur => (System.Windows.Media.RenderCapability.Tier >> 16) >= 2;

    /// <summary>Motion-blur radius for a speed (units/s): clamp(|v|·k, 0, max), 0 below a quarter pixel.</summary>
    public static double MotionBlur(double speed, double k, double max = 6)
    {
        var r = Math.Min(max, Math.Abs(speed) * k);
        return r < 0.25 ? 0 : r;
    }

    /// <summary>UI thread.</summary>
    public static event Action? ProfileChanged;

    /// <summary>Picks a token per profile (call sites whose Refreshed motion differs from the token-mapped Legacy one).</summary>
    public static MotionToken Pick(MotionToken legacy, MotionToken refreshed) => IsRefreshed ? refreshed : legacy;

    // ───────────── token tables ─────────────

    private static readonly Spring[] RefreshedSprings =
    [
        Spring.FromM3(800, 0.6),    // SpatialFast
        Spring.FromM3(380, 0.8),    // SpatialDefault
        Spring.FromM3(200, 0.8),    // SpatialSlow
        Spring.FromM3(3800, 1.0),   // EffectsFast
        Spring.FromM3(1600, 1.0),   // EffectsDefault
        Spring.FromM3(800, 1.0),    // EffectsSlow
        new(0.40, 0.80),            // Grow
        new(0.36, 0.78),            // GrowWide
        new(0.36, 0.92),            // Shrink
        new(0.40, 0.90),            // ShrinkWide
        new(0.30, 0.75),            // Small
        new(0.32, 0.86),            // ContentIn
        new(0.28, 0.62),            // Pop
        new(0.12, 1.0),             // Press
        new(0.26, 0.62),            // Release
        new(0.24, 1.0),             // Data
        new(0.10, 1.0),             // FxFast
        new(0.16, 1.0),             // Fx
        new(0.22, 1.0),             // FxSlow
    ];

    /// <summary>Refreshed physics spring of a token.</summary>
    public static Spring SpringOf(MotionToken token) => RefreshedSprings[(int)token];

    /// <summary>Legacy easing of a token (nearest M3 Expressive SpringEase).</summary>
    public static SpringEase LegacyOf(MotionToken token) => token switch
    {
        MotionToken.SpatialFast or MotionToken.Small or MotionToken.Pop or MotionToken.Release or MotionToken.Data => Springs.SpatialFast,
        MotionToken.SpatialSlow => Springs.SpatialSlow,
        MotionToken.EffectsFast or MotionToken.Press or MotionToken.FxFast => Springs.EffectsFast,
        MotionToken.EffectsDefault or MotionToken.Fx => Springs.EffectsDefault,
        MotionToken.EffectsSlow or MotionToken.FxSlow => Springs.EffectsSlow,
        _ => Springs.SpatialDefault,
    };

    // ───────────── token API (both profiles) ─────────────

    /// <summary>
    /// Animates a double DP to <paramref name="to"/>. Legacy: SpringEase storyboard (SnapshotAndReplace). Refreshed: one
    /// physics channel per (object, DP) — repeated calls retarget it and keep its velocity.
    /// </summary>
    public static void Animate(this DependencyObject target, DependencyProperty property, double to, MotionToken token, Action? completed = null)
    {
        if (IsRefreshed || target is not IAnimatable animatable)
        {
            Animate(target, property, to, SpringOf(token), 0, completed);
            return;
        }
        animatable.Animate(property, to, LegacyOf(token), completed);
    }

    /// <summary>Physics-spring animation (any profile) with an optional start delay in seconds.</summary>
    public static void Animate(this DependencyObject target, DependencyProperty property, double to, Spring spring, double delay = 0, Action? completed = null)
    {
        var sink = Sink(target, property, out var created);
        var channel = sink.Channel;
        if (!channel.IsAnimating)
        {
            // pick up the live value (a Legacy timeline, or a direct SetValue since the last run)
            var current = ReadAndClear(target, property);
            if (created || current != channel.Value) channel.Snap(current, notify: false);
        }
        channel.AnimateTo(to, spring, delay, completed);
    }

    /// <summary>Sets a value immediately, stopping any Legacy timeline or running channel on it.</summary>
    public static void Set(this DependencyObject target, DependencyProperty property, double value)
    {
        StopChannel(target, property);
        if (target is IAnimatable { HasAnimatedProperties: true } a) a.BeginAnimation(property, null);
        RemoveLegacy(target, property);
        target.SetValue(property, value);
    }

    /// <summary>True while a Legacy timeline or a Refreshed channel drives the property.</summary>
    public static bool IsAnimating(this DependencyObject target, DependencyProperty property)
    {
        if (Channels.TryGetValue(target, out var map) && map.TryGetValue(property, out var s) && s.Channel.IsAnimating) return true;
        return target is IAnimatable { HasAnimatedProperties: true } && FindLegacy(target, property) >= 0;
    }

    /// <summary>The channel driving (object, DP) under Refreshed (created on demand).</summary>
    public static SpringChannel Channel(this DependencyObject target, DependencyProperty property) => Sink(target, property, out _).Channel;

    /// <summary>Lands every running animation (both engines) on its target, firing completions.</summary>
    public static void SnapAll()
    {
        SpringAnimator.SnapAll();
        LandLegacy();
    }

    // ───────────── channel registry ─────────────

    private sealed class DpSink(DependencyObject target, DependencyProperty dp) : ISpringSink
    {
        public SpringChannel Channel = null!;
        public void OnSpringFrame() => target.SetValue(dp, Channel.Value);
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, DpSink>> Channels = new();

    private static DpSink Sink(DependencyObject target, DependencyProperty property, out bool created)
    {
        var map = Channels.GetValue(target, static _ => new Dictionary<DependencyProperty, DpSink>(2));
        if (map.TryGetValue(property, out var sink))
        {
            created = false;
            return sink;
        }
        sink = new DpSink(target, property);
        sink.Channel = new SpringChannel(ReadDouble(target, property), sink, EpsilonFor(property));
        map[property] = sink;
        created = true;
        return sink;
    }

    private static double EpsilonFor(DependencyProperty dp) =>
        dp == UIElement.OpacityProperty ? 1 / 512.0 :
        dp == System.Windows.Media.ScaleTransform.ScaleXProperty || dp == System.Windows.Media.ScaleTransform.ScaleYProperty ? 1 / 2000.0 :
        dp == System.Windows.Media.Effects.BlurEffect.RadiusProperty ? 0.05 : 0;

    private static double ReadDouble(DependencyObject target, DependencyProperty property) =>
        target.GetValue(property) is double d && !double.IsNaN(d) ? d : 0;

    private static double ReadAndClear(DependencyObject target, DependencyProperty property)
    {
        var current = ReadDouble(target, property);
        if (target is IAnimatable { HasAnimatedProperties: true } a && DependencyPropertyHelper.GetValueSource(target, property).IsAnimated)
        {
            a.BeginAnimation(property, null);
            RemoveLegacy(target, property);
            target.SetValue(property, current);
        }
        return current;
    }

    private static void StopChannel(DependencyObject target, DependencyProperty property)
    {
        if (Channels.TryGetValue(target, out var map) && map.TryGetValue(property, out var s) && s.Channel.IsAnimating)
            s.Channel.Snap(s.Channel.Value, notify: false);
    }

    // ───────────── Legacy timeline tracking (so a profile switch can land them) ─────────────

    private sealed class LegacyEntry(IAnimatable target, DependencyProperty property, double to, Action? completed)
    {
        public readonly IAnimatable Target = target;
        public readonly DependencyProperty Property = property;
        public readonly double To = to;
        public readonly Action? Completed = completed;
    }

    private static readonly List<LegacyEntry> Legacy = new(16);

    private static object TrackLegacy(IAnimatable target, DependencyProperty property, double to, Action? completed)
    {
        RemoveLegacy(target, property);
        var e = new LegacyEntry(target, property, to, completed);
        Legacy.Add(e);
        return e;
    }

    private static bool UntrackLegacy(object entry) => Legacy.Remove((LegacyEntry)entry);

    private static int FindLegacy(object target, DependencyProperty property)
    {
        for (int i = 0; i < Legacy.Count; i++)
            if (ReferenceEquals(Legacy[i].Target, target) && Legacy[i].Property == property) return i;
        return -1;
    }

    private static void RemoveLegacy(object target, DependencyProperty property)
    {
        var i = FindLegacy(target, property);
        if (i >= 0) Legacy.RemoveAt(i);
    }

    private static void LandLegacy()
    {
        if (Legacy.Count == 0) return;
        var all = Legacy.ToArray();
        Legacy.Clear();
        foreach (var e in all)
        {
            try
            {
                e.Target.BeginAnimation(e.Property, null);
                if (e.Target is DependencyObject d) d.SetValue(e.Property, e.To);
                e.Completed?.Invoke();
            }
            catch (Exception ex) { NotchHub.Core.Log.Debug("Legacy snap failed: " + ex.Message); }
        }
    }
}
