using System.Windows.Media;

namespace NotchHub.UI.Animation;

/// <summary>
/// Spring as (response T in seconds, damping ratio ζ); mass 1 ⇒ stiffness k = (2π/T)², damping c = 4πζ/T.
/// ζ &lt; 1 overshoots (0.8 → 1.5 %), ζ = 1 is critically damped. See docs/MOTION.md §2.
/// </summary>
public readonly record struct Spring(double Response, double DampingRatio)
{
    /// <summary>From a Material 3 (stiffness, damping ratio) pair.</summary>
    public static Spring FromM3(double stiffness, double ratio) => new(2 * Math.PI / Math.Sqrt(stiffness), ratio);

    /// <summary>Apple's (perceptual duration, bounce) model; bounce ≥ 0.</summary>
    public static Spring FromBounce(double duration, double bounce) => new(duration, 1 - bounce);

    public static Spring Of(SpringEase ease) => FromM3(ease.Stiffness, ease.DampingRatio);
}

/// <summary>Owner of one or more channels; called once per frame after its channels stepped.</summary>
public interface ISpringSink
{
    void OnSpringFrame();
}

/// <summary>
/// One interruptible physics value: value + velocity + target. Retargeting keeps value and velocity (C¹ continuity),
/// each frame advances with the exact closed-form solution (frame-rate independent). Delays are per-channel state.
/// </summary>
public sealed class SpringChannel
{
    private readonly double _fixedEpsilon;
    private double _epsilon = 0.1;
    private double _delay, _pendingTarget;
    private Spring _pendingSpring;
    private Action? _completed;
    internal bool Queued;

    /// <param name="epsilon">Rest threshold in value units; 0 = automatic (1/1000 of each move, clamped to 1e-4..0.1).</param>
    public SpringChannel(double value, ISpringSink sink, double epsilon = 0)
    {
        Value = Target = value;
        Sink = sink;
        _fixedEpsilon = epsilon;
        if (epsilon > 0) _epsilon = epsilon;
    }

    public double Value { get; private set; }
    /// <summary>Units per second; survives retargets.</summary>
    public double Velocity { get; private set; }
    public double Target { get; private set; }
    /// <summary>Target after a pending delay (or <see cref="Target"/>).</summary>
    public double FinalTarget => _delay > 0 ? _pendingTarget : Target;
    public Spring Spring { get; private set; } = new(0.3, 1);
    public ISpringSink Sink { get; }
    public bool IsAnimating => Queued;

    /// <summary>Raised when the channel comes to rest (after its value was written for that frame).</summary>
    public event Action<SpringChannel>? Settled;

    public void AnimateTo(double target, Spring spring, double delay = 0, Action? completed = null)
    {
        _completed = completed;
        if (Motion.ReduceMotion)
        {
            Land(target);
            return;
        }
        if (delay > 0)
        {
            _delay = delay;
            _pendingTarget = target;
            _pendingSpring = spring;
            UpdateEpsilon(target);
        }
        else
        {
            _delay = 0;
            Target = target;
            Spring = spring;
            UpdateEpsilon(target);
            if (!Queued && Velocity == 0 && Value == target)
            {
                FireCompleted();
                return;
            }
        }
        SpringAnimator.Enqueue(this);
    }

    /// <summary>Adds velocity (fling hand-off, "nudge") and lets the spring bring the value back to its target.</summary>
    public void AddVelocity(double v)
    {
        if (Motion.ReduceMotion) return;
        Velocity += v;
        if (_fixedEpsilon <= 0) _epsilon = Math.Max(1e-4, Math.Min(_epsilon, Math.Abs(v) / 1000));
        SpringAnimator.Enqueue(this);
    }

    /// <summary>Jumps to <paramref name="v"/> and stops (no completion, no Settled).</summary>
    public void Snap(double v, bool notify = true)
    {
        _delay = 0;
        _completed = null;
        Value = Target = v;
        Velocity = 0;
        if (Queued) SpringAnimator.Remove(this);
        if (notify) Sink.OnSpringFrame();
    }

    /// <summary>Lands on the final target now, firing completion + Settled (profile switch, reduce motion).</summary>
    public void Land() => Land(FinalTarget);

    private void Land(double target)
    {
        _delay = 0;
        Value = Target = target;
        Velocity = 0;
        if (Queued) SpringAnimator.Remove(this);
        Sink.OnSpringFrame();
        FireCompleted();
        Settled?.Invoke(this);
    }

    private void UpdateEpsilon(double target)
    {
        if (_fixedEpsilon > 0) return;
        var span = Math.Abs(target - Value);
        _epsilon = span > 0 ? Math.Clamp(span / 1000, 1e-4, 0.1) : Math.Min(_epsilon, 0.1);
    }

    private void FireCompleted()
    {
        var c = _completed;
        _completed = null;
        c?.Invoke();
    }

    /// <summary>Advances by <paramref name="dt"/> seconds; true when at rest.</summary>
    internal bool Step(double dt)
    {
        if (_delay > 0)
        {
            var remaining = _delay - dt;
            if (remaining > 0)
            {
                _delay = remaining;
                if (Velocity == 0 && Value == Target) return false;       // waiting at rest
                // otherwise keep moving toward the old target while the delay runs
            }
            else
            {
                _delay = 0;
                Target = _pendingTarget;
                Spring = _pendingSpring;
            }
        }

        double d = Value - Target, v = Velocity;
        double w0 = 2 * Math.PI / Math.Max(1e-3, Spring.Response), z = Math.Max(0, Spring.DampingRatio), nd, nv;
        if (z < 0.9999)
        {
            double wd = w0 * Math.Sqrt(1 - z * z), e = Math.Exp(-z * w0 * dt), c = Math.Cos(wd * dt), s = Math.Sin(wd * dt);
            nd = e * (d * c + (v + z * w0 * d) / wd * s);
            nv = e * (v * c - (w0 * w0 * d + z * w0 * v) / wd * s);
        }
        else if (z <= 1.0001)
        {
            double e = Math.Exp(-w0 * dt), b = v + w0 * d;
            nd = e * (d + b * dt);
            nv = e * (v - w0 * b * dt);
        }
        else
        {
            double r = w0 * Math.Sqrt(z * z - 1), r1 = -z * w0 + r, r2 = -z * w0 - r;
            double c1 = (v - r2 * d) / (r1 - r2), c2 = d - c1, e1 = Math.Exp(r1 * dt), e2 = Math.Exp(r2 * dt);
            nd = c1 * e1 + c2 * e2;
            nv = c1 * r1 * e1 + c2 * r2 * e2;
        }
        bool rest = _delay <= 0 && Math.Abs(nd) < _epsilon && Math.Abs(nv) < _epsilon * 62.5;
        Value = rest ? Target : Target + nd;
        Velocity = rest ? 0 : nv;
        return rest;
    }

    internal void OnRest()
    {
        FireCompleted();
        Settled?.Invoke(this);
    }
}

/// <summary>
/// The single per-frame driver for every <see cref="SpringChannel"/>: hooked on CompositionTarget.Rendering only while
/// something moves (idle ⇒ unhooked ⇒ no frames, ~0 CPU), advances by RenderingTime (deduped, dt clamped).
/// </summary>
public static class SpringAnimator
{
    private static readonly List<SpringChannel> Active = new(32);
    private static readonly List<ISpringSink> Dirty = new(8);
    private static readonly List<ISpringSink> Requests = new(4);
    private static readonly List<ISpringSink> RequestsWork = new(4);
    private static TimeSpan _last;
    private static bool _hooked, _errorLogged;

    /// <summary>Display frame period (s), used for the first frame after waking.</summary>
    public static double FramePeriod { get; set; } = 1 / 60.0;

    /// <summary>Dev slow motion (env NOTCHHUB_MOTION_SCALE, e.g. 0.2).</summary>
    public static double TimeScale { get; set; } = ReadScale();

    /// <summary>True while hooked to the render loop (must be false when idle).</summary>
    public static bool IsRunning => _hooked;

    public static int ActiveCount => Active.Count;

    /// <summary>Debug: raised once per processed frame with the real (unclamped, unscaled) dt in seconds.</summary>
    public static event Action<double>? FrameTick;

    /// <summary>Raised when the animator unhooks from the render loop (everything at rest).</summary>
    public static event Action? Idle;

    private static double ReadScale()
    {
        var s = Environment.GetEnvironmentVariable("NOTCHHUB_MOTION_SCALE");
        return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            && v > 0.01 && v <= 10 ? v : 1;
    }

    internal static void Enqueue(SpringChannel c)
    {
        if (!c.Queued)
        {
            c.Queued = true;
            Active.Add(c);
        }
        Hook();
    }

    internal static void Remove(SpringChannel c)
    {
        if (!c.Queued) return;
        c.Queued = false;
        Active.Remove(c);
    }

    /// <summary>Calls <paramref name="sink"/>.OnSpringFrame once on the next frame (coalesces several changes per frame).</summary>
    public static void RequestFrame(ISpringSink sink)
    {
        if (!Requests.Contains(sink)) Requests.Add(sink);
        Hook();
    }

    /// <summary>Lands every running channel on its final target (profile switch, screenshots).</summary>
    public static void SnapAll()
    {
        for (int guard = 0; Active.Count > 0 && guard < 10_000; guard++)
        {
            var c = Active[^1];
            c.Land();                                  // removes itself, fires completion / Settled (may enqueue more)
        }
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs re ? re.RenderingTime : TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
        if (now == _last) return;                                         // duplicate raise for the same frame
        double raw = _last == TimeSpan.Zero ? FramePeriod : (now - _last).TotalSeconds;
        _last = now;
        FrameTick?.Invoke(raw);
        double dt = Motion.ReduceMotion ? 10 : Math.Clamp(raw, 0, 0.05) * TimeScale;

        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (i >= Active.Count) continue;                              // list shrank in a callback
            var c = Active[i];
            bool rest = c.Step(dt);
            if (!Dirty.Contains(c.Sink)) Dirty.Add(c.Sink);
            if (!rest) continue;
            Active[i] = Active[^1];
            Active.RemoveAt(Active.Count - 1);
            c.Queued = false;
            Rested.Add(c);
        }
        // write values first, then notify rest (handlers may hide layers / start new motion)
        for (int i = 0; i < Dirty.Count; i++) Call(Dirty[i]);
        Dirty.Clear();
        FlushRests();

        if (Requests.Count > 0)
        {
            RequestsWork.AddRange(Requests);
            Requests.Clear();
            for (int i = 0; i < RequestsWork.Count; i++) Call(RequestsWork[i]);
            RequestsWork.Clear();
        }

        if (Active.Count == 0 && Requests.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;                    // zero idle CPU
            _hooked = false;
            Idle?.Invoke();
        }
    }

    private static readonly List<SpringChannel> Rested = new(16);

    private static readonly List<SpringChannel> RestedWork = new(16);

    private static void FlushRests()
    {
        if (Rested.Count == 0) return;
        RestedWork.AddRange(Rested);
        Rested.Clear();
        for (int i = 0; i < RestedWork.Count; i++)
        {
            var c = RestedWork[i];
            if (c.Queued) continue;                                        // retargeted by an earlier handler
            try { c.OnRest(); }
            catch (Exception ex) { LogOnce(ex); }
        }
        RestedWork.Clear();
    }

    private static void Call(ISpringSink sink)
    {
        try { sink.OnSpringFrame(); }
        catch (Exception ex) { LogOnce(ex); }
    }

    private static void LogOnce(Exception ex)
    {
        if (_errorLogged) return;
        _errorLogged = true;
        NotchHub.Core.Log.Error("Spring frame failed", ex);
    }
}
