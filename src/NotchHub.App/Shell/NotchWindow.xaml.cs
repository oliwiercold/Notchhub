using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NotchHub.App.Interop;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Features.Compact;
using NotchHub.UI;
using NotchHub.UI.Animation;

namespace NotchHub.App.Shell;

/// <summary>
/// The notch: a fixed-size transparent layered topmost tool window at the top-center of the notch monitor. The visible
/// body (<see cref="NotchShape"/>) morphs between the states of <see cref="ShellState"/>; transparent pixels are click-through.
/// </summary>
public partial class NotchWindow : Window, IShell
{
    public const double WindowWidth = 1000, WindowHeight = 660;
    private const double ExpandedWidth = 820, HeaderHeight = 46, MaxPanelHeight = 640, ContentPadV = 18, ContentPadH = 28;
    public const double LiveWidth = 300, LiveHeight = 34;

    public static readonly ShellTab[] StripTabs =
        [ShellTab.Controls, ShellTab.Today, ShellTab.Stats, ShellTab.Clipboard, ShellTab.Phone, ShellTab.Power];

    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _hoverTimer, _leaveTimer, _hudTimer;
    private readonly Dictionary<string, MeasureHost> _tabs = new();
    private readonly Dictionary<string, Button> _tabButtons = new();
    private IntPtr _hwnd;
    private volatile ShellState _state = ShellState.Collapsed;
    private volatile ShellTab _tab;
    private bool _tabShown;
    private bool _hovering, _pressed, _openedByHotkey, _fullscreen;
    private HudRequest? _hud;
    private FrameworkElement? _activeLayer;
    private LiveActivityView? _live;
    private PeekView? _peek;
    private HudView? _hudView;

    private EventHandler? _shellStateChanged;
    event EventHandler? IShell.StateChanged { add => _shellStateChanged += value; remove => _shellStateChanged -= value; }

    /// <summary>Screenshot mode: window placed off-screen, no auto-collapse.</summary>
    public bool ScreenshotMode { get; init; }
    /// <summary>Screenshot helper: render the plain collapsed notch even when media plays.</summary>
    internal bool SuppressLiveActivity { get; set; }

    public NotchWindow(ISettingsService settings)
    {
        _settings = settings;
        InitializeComponent();

        _hoverTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); OnHoverElapsed(); };
        _leaveTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); OnLeaveElapsed(); };
        _hudTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); OnHudElapsed(); };

        InitMotion();
        PeekHost.NaturalSizeChanged += (_, _) => { if (_activeLayer == PeekHost) ApplyGeometry(); };
        HudHost.NaturalSizeChanged += (_, _) => { if (_activeLayer == HudHost) ApplyGeometry(); };
        LiveLayer.NaturalSizeChanged += (_, _) => { if (_activeLayer == LiveLayer) ApplyGeometry(); };

        BuildHeader();
        _tab = InitialTab();
        _key = TabKey(_tab);

        Root.MouseEnter += (_, _) => OnPointerEnter();
        Root.MouseLeave += (_, _) => OnPointerLeave();
        Root.MouseWheel += OnWheel;
        Notch.MouseLeftButtonDown += OnNotchDown;
        Notch.MouseLeftButtonUp += OnNotchUp;
        HoverZone.MouseLeftButtonUp += OnNotchUp;
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (_state == ShellState.Expanded && !ScreenshotMode) Collapse(); };

        _settings.Changed += (_, _) => Dispatcher.BeginInvoke(OnSettingsChanged);
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            CreateLive();
            ApplyGeometry(instant: true);
        };
    }

    public IntPtr Handle => _hwnd;
    public ShellState State => _state;
    public ShellTab ActiveTab => _tab;

    // ───────────────────────────── window plumbing ─────────────────────────────

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetExStyleFlag(_hwnd, NativeMethods.WS_EX_TOOLWINDOW, true);
        NativeMethods.SetExStyleFlag(_hwnd, NativeMethods.WS_EX_APPWINDOW, false);
        NativeMethods.SetExStyleFlag(_hwnd, NativeMethods.WS_EX_NOACTIVATE, true);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Place();
        UpdateFramePeriod();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE when _state != ShellState.Expanded:
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            case NativeMethods.WM_DISPLAYCHANGE:
            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_SETTINGCHANGE:
                Dispatcher.BeginInvoke(() => { Place(); UpdateFramePeriod(); }, DispatcherPriority.Background);
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Top-center of the configured monitor, in physical pixels (per-monitor DPI aware).</summary>
    public void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (ScreenshotMode)
        {
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, -32000, -32000, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            return;
        }
        var mon = NativeMethods.GetNotchMonitor(_settings.Current.Notch.MonitorIndex);
        if (mon is null) return;
        int w = (int)Math.Round(WindowWidth * mon.Scale), h = (int)Math.Round(WindowHeight * mon.Scale);
        int x = mon.Bounds.Left + (mon.Bounds.Width - w) / 2, y = mon.Bounds.Top;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, w, h, NativeMethods.SWP_NOACTIVATE);
    }

    private void UpdateFramePeriod()
    {
        if (_hwnd == IntPtr.Zero) return;
        var hz = DisplayInfo.RefreshHz(_hwnd);
        SpringAnimator.FramePeriod = 1 / Math.Clamp(hz, 24, 1000);
    }

    private void SetActivatable(bool on)
    {
        if (_hwnd != IntPtr.Zero) NativeMethods.SetExStyleFlag(_hwnd, NativeMethods.WS_EX_NOACTIVATE, !on);
    }

    private void OnSettingsChanged()
    {
        Place();
        if (_state == ShellState.Collapsed) ApplyGeometry();
        UpdatePhoneChip();
        UpdateFullscreen(_fullscreen);
    }

    // ───────────────────────────── IShell ─────────────────────────────

    private void Run(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    public void Expand(ShellTab? tab = null, bool activate = true) => Run(() =>
    {
        if (_state == ShellState.Hidden && !_fullscreen) SetState(ShellState.Collapsed);
        if (tab is { } t) SelectTab(t);
        if (_state != ShellState.Expanded) _openedByHotkey = false;
        SetState(ShellState.Expanded);
        if (activate) ActivateNow();
    });

    /// <summary>Hotkey entry point: the panel stays open when the mouse is elsewhere.</summary>
    public void ToggleFromHotkey(ShellTab? tab) => Run(() =>
    {
        if (_state == ShellState.Expanded && (tab is null || _key == TabKey(tab.Value))) { Collapse(); return; }
        Expand(tab, true);
        _openedByHotkey = true;
    });

    public void Collapse() => Run(() =>
    {
        _openedByHotkey = false;
        SetState(_fullscreen && _settings.Current.Notch.HideInFullscreen ? ShellState.Hidden : ShellState.Collapsed);
    });

    public void Toggle(ShellTab? tab = null) => Run(() =>
    {
        if (_state == ShellState.Expanded && (tab is null || _key == TabKey(tab.Value))) Collapse();
        else Expand(tab);
    });

    public void ShowHud(HudRequest request) => Run(() =>
    {
        if (_state is ShellState.Expanded or ShellState.Hidden) return;
        _hud = request;
        EnsureHudView();
        Log.Safe("HUD present", () => _hudView!.ViewModel.Present(request));
        _hudTimer.Stop();
        _hudTimer.Interval = request.Duration ?? request.Kind switch
        {
            HudKind.PhoneNotification => TimeSpan.FromSeconds(5),
            HudKind.TimerFinished => TimeSpan.FromSeconds(10),
            HudKind.PhonePairRequest => TimeSpan.FromSeconds(30),
            HudKind.PhoneCharging => TimeSpan.FromSeconds(3),
            // a ringing call stays until the phone reports a new state (the app replaces / dismisses it)
            HudKind.PhoneCall => request.Payload is PhoneCall { State: PhoneCallState.Ringing } ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(1.6),
        };
        if (request.Kind == HudKind.PhonePairRequest && _hudTimer.Interval > TimeSpan.FromSeconds(30))
            _hudTimer.Interval = TimeSpan.FromSeconds(30);
        _hudTimer.Start();
        SetState(ShellState.Hud, force: true);
    });

    /// <summary>Kind of the HUD on screen (null when none).</summary>
    internal HudKind? CurrentHud => _state == ShellState.Hud ? _hud?.Kind : null;

    /// <summary>Dismisses the HUD if it shows <paramref name="kind"/> (e.g. a call that stopped ringing).</summary>
    internal void DismissHud(HudKind kind) => Run(() =>
    {
        if (_state == ShellState.Hud && _hud?.Kind == kind) OnHudElapsed();
    });

    private void OnHudElapsed()
    {
        _hud = null;
        if (_state != ShellState.Hud) return;
        if (_hovering && _settings.Current.Notch.Hover == HoverBehavior.PeekThenClick) SetState(ShellState.Peek);
        else SetState(ShellState.Collapsed);
    }

    /// <summary>Fullscreen app on the notch monitor (foreground service or AppBar notification).</summary>
    public void UpdateFullscreen(bool fullscreen) => Run(() =>
    {
        _fullscreen = fullscreen;
        bool hide = fullscreen && _settings.Current.Notch.HideInFullscreen;
        if (hide && _state != ShellState.Expanded && _state != ShellState.Hidden) SetState(ShellState.Hidden);
        else if (!hide && _state == ShellState.Hidden) SetState(ShellState.Collapsed);
    });

    private void ActivateNow()
    {
        SetActivatable(true);
        if (_hwnd != IntPtr.Zero) NativeMethods.ForceForeground(_hwnd);
        Activate();
        Focus();
    }

    // ───────────────────────────── state machine ─────────────────────────────

    internal void SetState(ShellState next, bool force = false)
    {
        var prev = _state;
        if (prev == next && !force) { ApplyGeometry(); return; }
        _state = next;
        if (next != ShellState.Expanded) SetActivatable(false);
        if (next != ShellState.Hud) { _hudTimer.Stop(); _hud = null; }

        if (next == ShellState.Hidden)
        {
            SwitchLayer(null, prev, instant: true);
            Hide();
        }
        else
        {
            if (prev == ShellState.Hidden || !IsVisible) { Show(); Place(); }
            ApplyGeometry();
        }
        if (prev != next) _shellStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasLiveActivity =>
        !SuppressLiveActivity && _live is not null && _settings.Current.Notch.ShowLiveActivity &&
        ReadBool(_live.ViewModel, "HasActivity");

    private static bool ReadBool(object? vm, string property) =>
        vm?.GetType().GetProperty(property)?.GetValue(vm) is true;

    /// <summary>
    /// Subtle edge glow, shown on hover and while open so the black notch never "disappears" over dark apps.
    /// Its strength adapts to the background next to the notch: full over near-black apps, faint over bright ones.
    /// </summary>
    private double _glowFactor = 1.0;
    private long _glowSampledAt;
    private int _glowSampling;

    private void SampleBackgroundForGlow()
    {
        if (_hwnd == IntPtr.Zero || ScreenshotMode) return;
        if (Environment.TickCount64 - Interlocked.Read(ref _glowSampledAt) < 1500) return;
        if (Interlocked.Exchange(ref _glowSampling, 1) == 1) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double w = Notch.BodyWidth, h = Notch.BodyHeight, rs = Notch.ShoulderRadius;
        var hwnd = _hwnd;
        Task.Run(() =>
        {
            try
            {
                if (!GetWindowRect(hwnd, out var r)) return;
                var cx = (r.Left + r.Right) / 2.0;
                (int, int) P(double dx, double dy) => ((int)Math.Round(cx + dx * scale), (int)Math.Round(r.Top + dy * scale));
                var points = new[]
                {
                    P(-(w / 2 + rs + 16), 4), P(w / 2 + rs + 16, 4),
                    P(-(w / 2 + 14), Math.Max(6, h * 0.6)), P(w / 2 + 14, Math.Max(6, h * 0.6)),
                    P(0, h + 14), P(-w / 4, h + 14), P(w / 4, h + 14),
                };
                if (ScreenSampler.AverageLuminance(points) is not { } lum) return;
                // near-black background → full glow; bright background → just a hint
                var factor = lum <= 0.08 ? 1.0 : lum >= 0.45 ? 0.3 : 1.0 - (lum - 0.08) / 0.37 * 0.7;
                Interlocked.Exchange(ref _glowSampledAt, Environment.TickCount64);
                Dispatcher.BeginInvoke(() =>
                {
                    if (Math.Abs(factor - _glowFactor) < 0.05) return;
                    _glowFactor = factor;
                    UpdateGlow();
                });
            }
            catch (Exception ex) { Log.Debug("glow sampling failed: " + ex.Message); }
            finally { Interlocked.Exchange(ref _glowSampling, 0); }
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    private void NotifyLayer(FrameworkElement layer, bool shown)
    {
        ITabView? view = null;
        if (layer == LiveLayer) view = _live;
        else if (layer == PeekHost) view = _peek;
        else if (layer == HudHost) view = _hudView;
        else if (layer == ExpandedLayer)
        {
            if (_tabs.TryGetValue(_key, out var host) && host.Child is ITabView tv)
            {
                if (shown == _tabShown) return;
                _tabShown = shown;
                view = tv;
            }
        }
        if (view is null) return;
        Log.Safe($"{(shown ? "OnShown" : "OnHidden")} {view.GetType().Name}", shown ? view.OnShown : view.OnHidden);
    }

    // ───────────────────────────── compact views ─────────────────────────────

    private void CreateLive()
    {
        if (_live is not null) return;
        try
        {
            _live = new LiveActivityView();
            LiveLayer.Child = _live;
            if (_live.DataContext is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is "HasActivity" or null or "")
                        Dispatcher.BeginInvoke(() => { if (_state == ShellState.Collapsed) ApplyGeometry(); else UpdateIdleHeight(); });
                };
        }
        catch (Exception ex) { Log.Error("LiveActivityView failed", ex); }
    }

    private void EnsurePeekView()
    {
        if (_peek is not null) return;
        try { _peek = new PeekView(); PeekHost.Child = _peek; }
        catch (Exception ex) { Log.Error("PeekView failed", ex); }
    }

    private void EnsureHudView()
    {
        if (_hudView is not null) return;
        try { _hudView = new HudView(); HudHost.Child = _hudView; }
        catch (Exception ex) { Log.Error("HudView failed", ex); }
    }

    // ───────────────────────────── pointer / keyboard ─────────────────────────────

    private void OnPointerEnter()
    {
        _hovering = true;
        _leaveTimer.Stop();
        if (_state == ShellState.Collapsed)
        {
            ApplyGeometry();
            var mode = _settings.Current.Notch.Hover;
            if (mode != HoverBehavior.ClickOnly)
            {
                _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, _settings.Current.Notch.HoverDelayMs));
                _hoverTimer.Start();
            }
        }
    }

    private void OnPointerLeave()
    {
        _hovering = false;
        _hoverTimer.Stop();
        if (_pressed) { _pressed = false; Squish(false); }
        if (_state == ShellState.Collapsed) ApplyGeometry();
        else if (_state == ShellState.Peek || (_state == ShellState.Expanded && !_openedByHotkey))
        {
            _leaveTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, _settings.Current.Notch.CollapseDelayMs));
            _leaveTimer.Start();
        }
    }

    private void OnHoverElapsed()
    {
        if (!_hovering || _state != ShellState.Collapsed) return;
        if (_settings.Current.Notch.Hover == HoverBehavior.ExpandOnHover) Expand(null, activate: false);
        else SetState(ShellState.Peek);
    }

    private void OnLeaveElapsed()
    {
        if (_hovering || ScreenshotMode) return;
        if (_state == ShellState.Peek) { SetState(ShellState.Collapsed); return; }
        if (_state != ShellState.Expanded || _openedByHotkey) return;
        if (KeepOpen()) { _leaveTimer.Start(); return; }
        Collapse();
    }

    /// <summary>Keep the panel open while the user types, drags or works in one of our popups / menus.</summary>
    private int _holds;

    public IDisposable HoldOpen()
    {
        Interlocked.Increment(ref _holds);
        return new HoldToken(this);
    }

    private sealed class HoldToken(NotchWindow owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref owner._holds);
        }
    }

    private bool KeepOpen() =>
        Volatile.Read(ref _holds) > 0 ||
        Keyboard.FocusedElement is TextBoxBase or PasswordBox ||
        Mouse.Captured is not null ||
        Mouse.DirectlyOver is not null; // pointer over one of our popups (the window itself reported leave)

    private void OnNotchDown(object sender, MouseButtonEventArgs e)
    {
        if (_state == ShellState.Expanded) return;
        _pressed = true;
        Squish(true);
    }

    private void OnNotchUp(object sender, MouseButtonEventArgs e)
    {
        if (_state == ShellState.Expanded) return;
        var wasPressed = _pressed || sender == HoverZone;
        _pressed = false;
        Squish(false);
        if (!wasPressed) return;
        e.Handled = true;
        if (_state == ShellState.Hud && _hud?.Kind is HudKind.PhonePairRequest or HudKind.PhoneCall) return;
        Expand(_state == ShellState.Hud && _hud?.Kind == HudKind.PhoneNotification ? ShellTab.Phone : null);
    }

    private void Squish(bool down)
    {
        if (down)
        {
            NotchScale.Animate(ScaleTransform.ScaleXProperty, 0.97, MotionToken.Press);
            NotchScale.Animate(ScaleTransform.ScaleYProperty, Motion.IsRefreshed ? 0.92 : 0.9, MotionToken.Press);
        }
        else
        {
            NotchScale.Animate(ScaleTransform.ScaleXProperty, 1, MotionToken.Release);
            NotchScale.Animate(ScaleTransform.ScaleYProperty, 1, MotionToken.Release);
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta >= 0 || _state == ShellState.Expanded || !_settings.Current.Notch.ScrollToExpand) return;
        e.Handled = true;
        Expand(null, activate: true);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_state != ShellState.Expanded) return;
        if (e.Key == Key.Escape) { Collapse(); e.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var keys = TabKeys();
        if (e.Key == Key.Tab)
        {
            var i = keys.IndexOf(_key);
            var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1;
            SelectKey(keys[((i < 0 ? 0 : i) + step + keys.Count) % keys.Count]);
            e.Handled = true;
        }
        else if (e.Key is >= Key.D1 and <= Key.D9 || e.Key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            var idx = e.Key >= Key.NumPad1 ? e.Key - Key.NumPad1 : e.Key - Key.D1;
            if (idx < keys.Count) SelectKey(keys[idx]);
            e.Handled = true;
        }
    }
}
