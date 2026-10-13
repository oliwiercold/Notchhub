using System.Windows;
using System.Windows.Threading;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Animation;

namespace NotchHub.App.Shell;

/// <summary>
/// Geometry + choreography (docs/MOTION.md §4). One engine for every profile: Refreshed/Smooth retarget physics channels
/// keeping their velocity, with per-transition springs and lead/lag delays; Legacy uses the M3 springs it always had and
/// drops the velocity on every retarget (the old SnapshotAndReplace feel). Smooth adds motion blur on moving content.
/// </summary>
public partial class NotchWindow
{
    public const double LiveMaxWidth = 560;

    private LayerMotion _liveM = null!, _peekM = null!, _hudM = null!, _expM = null!, _bodyM = null!;
    private ShellState _geomState = ShellState.Collapsed;
    private bool _geomLive;
    private double _idleHeight = -1;
    private DispatcherTimer? _idleTimer;

    // frame statistics (NOTCHHUB_FPS=1 or --verbose): fps of each animation burst vs the display refresh rate
    private int _fpsFrames, _fpsDropped;
    private double _fpsSum, _fpsWorst;

    /// <summary>Height (DIP) of the idle notch: plain height when empty, live-activity height while it shows something.</summary>
    public double IdleHeight => _idleHeight < 0 ? _settings.Current.Notch.CollapsedHeight : _idleHeight;

    /// <summary>Raised (UI thread) when <see cref="IdleHeight"/> changes (debounced when it shrinks).</summary>
    public event EventHandler? IdleHeightChanged;

    private void InitMotion()
    {
        Notch.StageWidth = WindowWidth;
        Notch.StageHeight = WindowHeight;
        _liveM = new LayerMotion(LiveLayer, () => _activeLayer == LiveLayer, Notch, entranceBlur: true, maxMotionBlur: 6);
        _peekM = new LayerMotion(PeekHost, () => _activeLayer == PeekHost, Notch, entranceBlur: true, maxMotionBlur: 6);
        _hudM = new LayerMotion(HudHost, () => _activeLayer == HudHost, Notch, entranceBlur: true, maxMotionBlur: 6);
        _expM = new LayerMotion(ExpandedLayer, () => _activeLayer == ExpandedLayer, Notch, entranceBlur: false, maxMotionBlur: 3);
        _bodyM = new LayerMotion(TabBody, () => true, null, entranceBlur: false, maxMotionBlur: 0);
        _bodyM.Snap(1);
        Notch.Frame += () =>
        {
            if (!Motion.IsSmooth) return;
            if (_activeLayer is not null) M(_activeLayer).UpdateBlur();
        };
        Notch.Bloom = Motion.IsRefreshed;
        Motion.ProfileChanged += () => Notch.Bloom = Motion.IsRefreshed;

        if (Environment.GetEnvironmentVariable("NOTCHHUB_FPS") == "1" || Log.Verbose)
        {
            SpringAnimator.FrameTick += dt =>
            {
                _fpsFrames++;
                if (_fpsFrames == 1) return;                 // first dt is the assumed period
                _fpsSum += dt;
                _fpsWorst = Math.Max(_fpsWorst, dt);
                if (dt > 1.5 * SpringAnimator.FramePeriod) _fpsDropped++;
            };
            SpringAnimator.Idle += () =>
            {
                if (_fpsFrames > 6 && _fpsSum > 0)
                    Log.Info(FormattableString.Invariant(
                        $"Motion burst: {_fpsFrames} frames, {(_fpsFrames - 1) / _fpsSum:0.0} fps (display {1 / SpringAnimator.FramePeriod:0} Hz), worst {_fpsWorst * 1000:0.0} ms, dropped {_fpsDropped}, profile {Motion.Profile}"));
                _fpsFrames = _fpsDropped = 0;
                _fpsSum = _fpsWorst = 0;
            };
        }
    }

    private LayerMotion M(FrameworkElement layer) =>
        layer == LiveLayer ? _liveM : layer == PeekHost ? _peekM : layer == HudHost ? _hudM : _expM;

    private static Spring S(double response, double damping) => new(response, damping);
    private static Spring Tk(MotionToken t) => Motion.SpringOf(t);

    /// <summary>Retarget (Refreshed: keeps velocity) or restart from rest (Legacy). Instant / ReduceMotion lands now.</summary>
    private static void Go(SpringChannel c, double target, Spring spring, double delay = 0, bool instant = false)
    {
        if (instant || Motion.ReduceMotion)
        {
            c.Snap(target);
            return;
        }
        if (!Motion.IsRefreshed) c.Snap(c.Value, notify: false);    // Legacy: velocity lost on interruption
        c.AnimateTo(target, spring, delay);
    }

    /// <summary>Chooses the content layer for the current state and springs the body to its size.</summary>
    private void ApplyGeometry(bool instant = false)
    {
        UpdateGlow();
        var n = _settings.Current.Notch;
        FrameworkElement? layer;
        double w, h, rb, rs;
        bool live = false;

        switch (_state)
        {
            case ShellState.Peek:
                EnsurePeekView();
                layer = PeekHost;
                (w, h) = Natural(PeekHost, 300, 64, ExpandedWidth);
                (rb, rs) = (28, 12);
                break;
            case ShellState.Hud:
                EnsureHudView();
                layer = HudHost;
                (w, h) = Natural(HudHost, 240, 48, ExpandedWidth);
                (rb, rs) = (28, 12);
                break;
            case ShellState.Expanded:
                layer = ExpandedLayer;
                var host = EnsureActiveHost();
                host.Visibility = Visibility.Visible;
                host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // the real desired height of the tab (a small sane minimum only — no forced 160 px of empty space)
                var content = Math.Max(64, host.NaturalSize.Height);
                w = ExpandedWidth;
                h = Math.Min(MaxPanelHeight, HeaderHeight + ContentPadV + content);
                (rb, rs) = (32, 14);
                break;
            default:
                if (HasLiveActivity)
                {
                    layer = LiveLayer;
                    live = true;
                    (w, h) = LiveSize();
                    (rb, rs) = (h / 2, 8);
                }
                else
                {
                    layer = null;
                    (w, h, rb, rs) = (Math.Max(40, n.CollapsedWidth), Math.Max(4, n.CollapsedHeight), 9, 6);
                }
                if (_hovering && !_pressed)
                {
                    // the notch "breathes" under the pointer
                    w += Motion.IsRefreshed ? 6 : 4;
                    h += 2;
                }
                break;
        }

        if (layer is not null)
        {
            if (layer.Width != w) layer.Width = w;
            if (layer.Height != h) layer.Height = h;
        }

        var from = _geomState;
        var fromLive = _geomLive;
        _geomState = _state;
        _geomLive = live;
        instant |= Motion.ReduceMotion;

        Morph(from, fromLive, live, w, h, rb, rs, instant);
        SwitchLayer(layer, from, instant);

        // hover zone: a little larger than the collapsed body
        HoverZone.Width = Math.Max(w, n.CollapsedWidth) + 60;
        HoverZone.Height = Math.Max(Math.Min(h, live ? h : LiveHeight), n.CollapsedHeight) + 14;

        // stroke shadow under the open panel (no DropShadowEffect on the morphing body)
        if (_state == ShellState.Expanded) Go(Notch.Shadow, 1, Tk(MotionToken.FxSlow), Motion.IsRefreshed ? 0.22 : 0, instant);
        else Go(Notch.Shadow, 0, Tk(MotionToken.FxFast), 0, instant);

        UpdateIdleHeight();
    }

    private (double W, double H) LiveSize()
    {
        // measure the view itself (works while the layer is collapsed, e.g. for the AppBar height)
        if (LiveLayer.Child is not UIElement view) return (LiveWidth, LiveHeight);
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = view.DesiredSize;
        double w = size.Width > 0 ? size.Width : LiveWidth, h = size.Height > 0 ? size.Height : LiveHeight;
        return (Math.Clamp(w, 200, LiveMaxWidth), Math.Clamp(h, 28, 48));
    }

    private void Morph(ShellState from, bool fromLive, bool live, double w, double h, double rb, double rs, bool instant)
    {
        Spring sw, sh, sr;
        double dw = 0, dh = 0, dr = 0;
        bool growW = w > Notch.W.FinalTarget, growH = h > Notch.H.FinalTarget;
        var to = _state;

        if (!Motion.IsRefreshed)
        {
            // Legacy: one spring for everything (fast for peek, HUD and hover)
            sw = sh = sr = Tk(to is ShellState.Peek or ShellState.Hud || (to == ShellState.Collapsed && from == ShellState.Collapsed)
                ? MotionToken.SpatialFast : MotionToken.SpatialDefault);
        }
        else if (to == ShellState.Expanded)
        {
            sw = Tk(growW ? MotionToken.GrowWide : MotionToken.ShrinkWide);
            sh = from == ShellState.Expanded ? S(0.38, 0.85) : Tk(growH ? MotionToken.Grow : MotionToken.Shrink);
            sr = sh;
        }
        else if (to == ShellState.Peek)
        {
            sw = Tk(MotionToken.Small);
            (sh, sr) = from == ShellState.Peek ? (Tk(MotionToken.Small), Tk(MotionToken.Small)) : (S(0.38, 0.80), S(0.30, 0.90));
        }
        else if (to == ShellState.Hud)
        {
            sw = Tk(MotionToken.Small);
            sh = sr = from == ShellState.Hud ? Tk(MotionToken.Small) : S(0.32, 0.80);
        }
        else
        {
            switch (from)
            {
                case ShellState.Expanded:
                    (sh, dh) = (Tk(MotionToken.Shrink), 0.04);
                    (sw, dw) = (Tk(MotionToken.ShrinkWide), 0.07);
                    (sr, dr) = (S(0.30, 1.0), 0.04);
                    break;
                case ShellState.Peek:
                    (sh, dh) = (S(0.32, 0.95), 0.03);
                    (sw, dw) = (S(0.34, 0.92), 0.05);
                    (sr, dr) = (sh, dh);
                    break;
                case ShellState.Hud:
                    (sh, dh) = (S(0.34, 0.95), 0.04);
                    sw = S(0.34, 0.95);
                    (sr, dr) = (sh, dh);
                    break;
                default:
                    if (live && !fromLive) { sw = S(0.40, 0.75); sh = sr = S(0.36, 0.85); }
                    else if (!live && fromLive) { sw = S(0.34, 0.95); (sh, dh) = (S(0.30, 1.0), 0.06); (sr, dr) = (sh, dh); }
                    else sw = sh = sr = S(0.30, 0.70);       // hover breathing / live width change
                    break;
            }
        }

        Go(Notch.W, w, sw, dw, instant);
        Go(Notch.H, h, sh, dh, instant);
        Go(Notch.Rb, rb, sh, dh, instant);
        Go(Notch.Rs, rs, sr, dr, instant);
    }

    private static (double, double) Natural(MeasureHost host, double minW, double minH, double maxW)
    {
        host.Visibility = Visibility.Visible;
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = host.NaturalSize;
        if (size.Width <= 0) size.Width = minW;
        if (size.Height <= 0) size.Height = minH;
        return (Math.Min(maxW, Math.Max(minW, size.Width)), Math.Min(MaxPanelHeight, Math.Max(minH, size.Height)));
    }

    // ───────────────────────────── layers ─────────────────────────────

    private void SwitchLayer(FrameworkElement? layer, ShellState from, bool instant)
    {
        if (layer == _activeLayer) return;
        var old = _activeLayer;
        _activeLayer = layer;

        if (old is not null)
        {
            NotifyLayer(old, shown: false);
            var m = M(old);
            if (instant) m.Snap(0);
            else
            {
                LayerOut(m, old, layer);
                m.CheckHidden();
            }
        }

        if (layer is null) return;
        var nm = M(layer);
        nm.Show();
        NotifyLayer(layer, shown: true);
        if (instant)
        {
            nm.Snap(1);
            if (layer == ExpandedLayer) _bodyM.Snap(1);
            return;
        }
        LayerIn(nm, layer, from);
    }

    private void LayerOut(LayerMotion m, FrameworkElement old, FrameworkElement? next)
    {
        if (!Motion.IsRefreshed)
        {
            Go(m.Alpha, 0, S(0.09, 1));
            return;
        }
        Go(m.Alpha, 0, Tk(MotionToken.FxFast));
        if (old == ExpandedLayer)
        {
            Go(m.Scale, 0.97, S(0.24, 1));
            Go(m.Y, -8, S(0.24, 1));
        }
        else if (old == PeekHost && next == ExpandedLayer)
        {
            Go(m.Scale, 1.04, Tk(MotionToken.FxFast));              // zoom-through, like pressing into the island
            Go(m.Blur, 6, Tk(MotionToken.FxFast));
        }
        else if (old == LiveLayer)
        {
            Go(m.Scale, 0.8, Tk(MotionToken.FxFast));
            Go(m.Blur, 4, Tk(MotionToken.FxFast));
        }
        else Go(m.Blur, 6, Tk(MotionToken.FxFast));
    }

    private void LayerIn(LayerMotion m, FrameworkElement layer, ShellState from)
    {
        var fresh = m.IsOut;   // re-selected mid-exit → reverse from the current values (no blink)
        if (!Motion.IsRefreshed)
        {
            if (fresh) m.Snap(0, 0.97, 0, -8);
            var e = S(0.18, 1);
            Go(m.Alpha, 1, e, 0.06);
            Go(m.Scale, 1, e, 0.06);
            Go(m.Y, 0, e, 0.06);
            Go(m.Blur, 0, e);
            if (layer == ExpandedLayer) _bodyM.Snap(1);
            return;
        }

        var fx = Tk(MotionToken.Fx);
        var contentIn = Tk(MotionToken.ContentIn);
        if (layer == PeekHost)
        {
            if (fresh) m.Snap(0, 0.92, 0, -6, 8);
            const double d = 0.07;
            Go(m.Alpha, 1, fx, d);
            Go(m.Scale, 1, contentIn, d);
            Go(m.Y, 0, contentIn, d);
            Go(m.Blur, 0, Tk(MotionToken.FxSlow), d);
        }
        else if (layer == HudHost)
        {
            if (fresh) m.Snap(0, 0.96, 0, -4, 6);
            const double d = 0.05;
            Go(m.Alpha, 1, fx, d);
            Go(m.Scale, 1, Tk(MotionToken.Pop), d);
            Go(m.Y, 0, contentIn, d);
            Go(m.Blur, 0, fx, d);
        }
        else if (layer == LiveLayer)
        {
            if (fresh) m.Snap(0, 0.9, 0, 0, 6);
            var d = from == ShellState.Expanded ? 0.18 : 0.09;
            Go(m.Alpha, 1, fx, d);
            Go(m.Scale, 1, Tk(MotionToken.Pop), d);
            Go(m.Y, 0, contentIn, d);
            Go(m.Blur, 0, fx, d);
        }
        else
        {
            if (fresh)
            {
                m.Snap(0, 1, 0, -8);
                _bodyM.Snap(0, 0.96, 0, -12);
            }
            var d = from == ShellState.Peek ? 0.08 : 0.10;
            Go(m.Alpha, 1, fx, d);
            Go(m.Y, 0, contentIn, d);
            Go(m.Scale, 1, contentIn, d);
            Go(_bodyM.Alpha, 1, fx, d + 0.04);
            Go(_bodyM.Scale, 1, contentIn, d + 0.04);
            Go(_bodyM.Y, 0, contentIn, d + 0.04);
        }
    }

    // ───────────────────────────── glow ─────────────────────────────

    private void UpdateGlow()
    {
        var target = _state switch
        {
            ShellState.Collapsed => _hovering ? 1.0 : 0.0,
            ShellState.Hidden => 0.0,
            _ => 0.7,
        };
        if (target > 0) SampleBackgroundForGlow();
        Go(Notch.Glow, target * _glowFactor, Tk(MotionToken.FxSlow));
    }

    // ───────────────────────────── reserved space (AppBar) ─────────────────────────────

    private void UpdateIdleHeight()
    {
        var n = _settings.Current.Notch;
        var h = HasLiveActivity ? LiveSize().H : Math.Max(4, n.CollapsedHeight);
        if (Math.Abs(h - IdleHeight) < 0.5 && _idleHeight >= 0) { _idleTimer?.Stop(); return; }
        if (_idleHeight < 0 || h > _idleHeight)
        {
            _idleTimer?.Stop();
            SetIdleHeight(h);
            return;
        }
        // shrinking: wait a moment so a track change / short pause does not re-fit every maximized window twice
        if (_idleTimer is null)
        {
            _idleTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
            _idleTimer.Tick += (_, _) =>
            {
                _idleTimer!.Stop();
                var cur = HasLiveActivity ? LiveSize().H : Math.Max(4, _settings.Current.Notch.CollapsedHeight);
                SetIdleHeight(cur);
            };
        }
        if (!_idleTimer.IsEnabled) _idleTimer.Start();
    }

    private void SetIdleHeight(double h)
    {
        if (Math.Abs(h - _idleHeight) < 0.5) return;
        _idleHeight = h;
        IdleHeightChanged?.Invoke(this, EventArgs.Empty);
    }
}
