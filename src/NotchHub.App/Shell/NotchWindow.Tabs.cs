using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI;
using NotchHub.UI.Animation;
using NotchHub.UI.Localization;
using NotchHub.UI.Modding;

namespace NotchHub.App.Shell;

/// <summary>
/// Header tab strip: the built-in tabs followed by mod tabs (<see cref="ModRegistry.Tabs"/>, live). Tabs are addressed by a
/// key: the <see cref="ShellTab"/> name for built-in tabs, "mod:&lt;id&gt;" for mod tabs.
/// </summary>
public partial class NotchWindow
{
    private const string ModPrefix = "mod:";
    private string _key = nameof(ShellTab.Controls);
    private readonly Dictionary<MeasureHost, LayerMotion> _tabMotion = new();
    private readonly List<string> _modKeys = new();

    public NotchWindow() : this(AppServices.Settings) { }

    private static string TabKey(ShellTab t) => t.ToString();

    private List<string> TabKeys()
    {
        var keys = new List<string>(StripTabs.Length + _modKeys.Count);
        foreach (var t in StripTabs) keys.Add(TabKey(t));
        keys.AddRange(_modKeys);
        return keys;
    }

    private ShellTab InitialTab()
    {
        var n = _settings.Current.Notch;
        if (n.RememberLastTab && Enum.TryParse<ShellTab>(n.LastTab, true, out var last)) return last;
        return Enum.TryParse<ShellTab>(n.DefaultTab, true, out var def) ? def : ShellTab.Controls;
    }

    private void BuildHeader()
    {
        foreach (var tab in StripTabs)
        {
            // every label uses the same weight, so the measured width never differs from what is drawn
            var text = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = NhFonts.Sans };
            text.SetResourceReference(TextBlock.TextProperty, $"shell.tab.{tab.ToString().ToLowerInvariant()}");
            AddTabButton(TabKey(tab), text, new Thickness(14, 0, 14, 0));
        }
        TabButtons.SizeChanged += (_, _) => UpdateTabVisuals(instant: _state != ShellState.Expanded);
        GearButton.Click += (_, _) =>
        {
            if (_key != TabKey(ShellTab.Settings)) SelectTab(ShellTab.Settings);
            else SelectTab(InitialStripTab());
        };
        PhoneChip.Click += (_, _) => SelectTab(ShellTab.Phone);
        GearButton.ToolTip = null;
        Loaded += (_, _) => { UpdateTabVisuals(instant: true); UpdatePhoneChip(); };

        ModRegistry.Tabs.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(SyncModTabs);
        ModRegistry.TabOpenRequested += (_, id) => Dispatcher.BeginInvoke(() => OpenModTab(id));
        SyncModTabs();
    }

    private Button AddTabButton(string key, UIElement content, Thickness padding)
    {
        var button = new Button
        {
            Style = (Style)FindResource("PlainButton"),
            Content = content,
            Padding = padding,
            Height = 32,
            Margin = new Thickness(2, 0, 2, 0),          // ~4 px between pills
            Background = Brushes.Transparent,
        };
        Tactile.SetSquish(button, 0.06);
        button.Click += (_, _) => SelectKey(key);
        // recompute the pill after layout (language switch, font fallback, mod tabs added)
        button.SizeChanged += (_, _) => { if (_key == key) UpdateTabVisuals(instant: _state != ShellState.Expanded); };
        _tabButtons[key] = button;
        TabButtons.Children.Add(button);
        return button;
    }

    /// <summary>Mirrors <see cref="ModRegistry.Tabs"/> into the strip (icon buttons after the built-in tabs).</summary>
    private void SyncModTabs()
    {
        var wanted = new List<string>();
        foreach (var t in ModRegistry.Tabs) wanted.Add(ModPrefix + t.Id);

        foreach (var key in _modKeys.Where(k => !wanted.Contains(k)).ToList())
        {
            if (_tabButtons.Remove(key, out var b)) TabButtons.Children.Remove(b);
            if (_tabs.Remove(key, out var host))
            {
                if (_key == key && _tabShown && host.Child is ITabView tv) Log.Safe("OnHidden mod tab", tv.OnHidden);
                _tabMotion.Remove(host);
                TabStage.Children.Remove(host);
            }
            if (_key == key) SelectTab(_tab);
        }

        foreach (var t in ModRegistry.Tabs)
        {
            var key = ModPrefix + t.Id;
            if (_tabButtons.TryGetValue(key, out var existing))
            {
                TabButtons.Children.Remove(existing);       // re-append in registry order
                TabButtons.Children.Add(existing);
                continue;
            }
            var icon = new UI.Controls.Icon { Glyph = string.IsNullOrWhiteSpace(t.Icon) ? "extension" : t.Icon, Size = 18 };
            var b = AddTabButton(key, icon, new Thickness(9, 0, 9, 0));
            b.ToolTip = t.Title;
        }
        _modKeys.Clear();
        _modKeys.AddRange(wanted);
        UpdateTabVisuals(instant: _state != ShellState.Expanded);
    }

    private void OpenModTab(string id)
    {
        var key = ModPrefix + id;
        if (!_modKeys.Contains(key)) return;
        SelectKey(key);
        Expand(null, activate: true);
    }

    private ShellTab InitialStripTab()
    {
        var t = InitialTab();
        return t == ShellTab.Settings ? ShellTab.Controls : t;
    }

    /// <summary>Phone chip: battery + notification badge; hidden without a paired phone.</summary>
    public void UpdatePhoneChip()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(UpdatePhoneChip); return; }
        var phone = AppServices.Phone;
        var device = phone?.ActiveDevice;
        if (phone is null || device is null || !_settings.Current.Phone.ShowStatusInHeader)
        {
            PhoneChip.Visibility = Visibility.Collapsed;
            return;
        }
        PhoneChip.Visibility = Visibility.Visible;
        var battery = phone.Battery;
        PhoneBattery.Text = battery is null ? "" : $"{battery.Percent}%";
        PhoneBattery.Visibility = battery is null ? Visibility.Collapsed : Visibility.Visible;
        PhoneBolt.Visibility = battery?.IsCharging == true ? Visibility.Visible : Visibility.Collapsed;
        var count = phone.Notifications.Count;
        PhoneBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PhoneBadgeText.Text = count > 99 ? "99+" : count.ToString(Loc.Culture);
    }

    private FrameworkElement CreateView(string key)
    {
        if (key.StartsWith(ModPrefix, StringComparison.Ordinal))
        {
            var mod = ModRegistry.Tabs.FirstOrDefault(t => ModPrefix + t.Id == key);
            return mod?.CreateView() ?? new Border();
        }
        return Enum.Parse<ShellTab>(key) switch
        {
            ShellTab.Controls => new Features.Controls.ControlsView(),
            ShellTab.Today => new Features.Today.TodayView(),
            ShellTab.Stats => new Features.Stats.StatsView(),
            ShellTab.Clipboard => new Features.Clipboard.ClipboardView(),
            ShellTab.Phone => new Features.Phone.PhoneView(),
            ShellTab.Power => new Features.Power.PowerView(),
            _ => new Features.Settings.SettingsView(),
        };
    }

    private MeasureHost EnsureTab(string key)
    {
        if (_tabs.TryGetValue(key, out var existing)) return existing;
        FrameworkElement view;
        try { view = CreateView(key); }
        catch (Exception ex)
        {
            Log.Error($"Creating {key} view failed", ex);
            view = new Border();
        }
        var host = new MeasureHost
        {
            Child = view,
            FixedWidth = ExpandedWidth - ContentPadH,
            MaxChildHeight = MaxPanelHeight - HeaderHeight - ContentPadV,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
        };
        host.NaturalSizeChanged += (_, _) =>
        {
            if (_state == ShellState.Expanded && _tabs.GetValueOrDefault(_key) == host) ApplyGeometry();
        };
        _tabs[key] = host;
        _tabMotion[host] = new LayerMotion(host, () => _tabs.GetValueOrDefault(_key) == host, null, entranceBlur: false, maxMotionBlur: 2.5);
        TabStage.Children.Add(host);
        return host;
    }

    private MeasureHost EnsureActiveHost()
    {
        if (_key.StartsWith(ModPrefix, StringComparison.Ordinal) && !_modKeys.Contains(_key)) _key = TabKey(_tab);
        return EnsureTab(_key);
    }

    /// <summary>Switches the tab (cross-fade + slide) and springs the panel height to the new content.</summary>
    public void SelectTab(ShellTab tab) => SelectKey(TabKey(tab));

    private void SelectKey(string key)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SelectKey(key)); return; }
        var prev = _key;
        var newHost = EnsureTab(key);
        if (prev == key && newHost.Visibility == Visibility.Visible && _tabMotion[newHost].Alpha.FinalTarget > 0.5) return;

        var visible = _state == ShellState.Expanded;
        _tabs.TryGetValue(prev, out var oldHost);
        var dir = Math.Sign(Order(key) - Order(prev));
        var instant = !visible || Motion.ReduceMotion;
        var refreshed = Motion.IsRefreshed;

        if (oldHost is not null && oldHost != newHost)
        {
            if (visible && _tabShown && oldHost.Child is ITabView ov) Log.Safe("OnHidden tab", ov.OnHidden);
            var om = _tabMotion[oldHost];
            _key = key;                                    // so the old host is no longer "wanted" when it settles
            if (instant) om.Snap(0);
            else
            {
                Go(om.Alpha, 0, refreshed ? Tk(MotionToken.FxFast) : S(0.09, 1));
                Go(om.X, (refreshed ? -16 : -24) * dir, S(0.26, 1.0));
                om.CheckHidden();
            }
        }

        _key = key;
        if (Enum.TryParse<ShellTab>(key, out var builtIn)) _tab = builtIn;
        var nm = _tabMotion[newHost];
        nm.Show();
        if (instant) nm.Snap(1);
        else
        {
            var fresh = nm.IsOut;
            if (fresh) nm.Snap(0, 1, 24 * dir, 0);
            Go(nm.Alpha, 1, refreshed ? Tk(MotionToken.Fx) : S(0.18, 1), refreshed ? 0.04 : 0.06);
            Go(nm.X, 0, refreshed ? S(0.34, 0.88) : Tk(MotionToken.SpatialDefault), refreshed ? 0.04 : 0);
        }
        if (visible)
        {
            _tabShown = true;
            if (newHost.Child is ITabView nv) Log.Safe("OnShown tab", nv.OnShown);
        }
        else _tabShown = false;

        UpdateTabVisuals(instant: !visible);
        if (_settings.Current.Notch.RememberLastTab && key != TabKey(ShellTab.Settings) && !key.StartsWith(ModPrefix, StringComparison.Ordinal) &&
            !string.Equals(_settings.Current.Notch.LastTab, key, StringComparison.Ordinal) && !ScreenshotMode)
            _settings.Update(s => s.Notch.LastTab = key);
        if (visible) ApplyGeometry();
    }

    private int Order(string key)
    {
        if (key == TabKey(ShellTab.Settings)) return int.MaxValue / 2;
        var i = TabKeys().IndexOf(key);
        return i < 0 ? 0 : i;
    }

    private void UpdateTabVisuals(bool instant)
    {
        foreach (var (key, button) in _tabButtons)
        {
            var brush = key == _key ? "Brush.OnSecondaryContainer" : "Brush.OnSurfaceVariant";
            if (button.Content is TextBlock tb) tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            else if (button.Content is UI.Controls.Icon ic) ic.SetResourceReference(UI.Controls.Icon.ForegroundProperty, brush);
        }
        var settingsActive = _key == TabKey(ShellTab.Settings);
        if (settingsActive) GearButton.SetResourceReference(BackgroundProperty, "Brush.SecondaryContainer");
        else GearButton.Background = Brushes.Transparent;
        GearIcon.SetResourceReference(UI.Controls.Icon.ForegroundProperty, settingsActive ? "Brush.OnSecondaryContainer" : "Brush.OnSurfaceVariant");

        instant |= Motion.ReduceMotion;
        if (!_tabButtons.TryGetValue(_key, out var target) || !target.IsLoaded || target.ActualWidth <= 0)
        {
            Go(TabIndicator.Alpha, 0, Tk(MotionToken.FxFast), 0, instant);
            return;
        }
        // layout position (sum of the previous slots) — not TranslatePoint, which would include the press-squish transform
        double x = 0;
        foreach (UIElement child in TabButtons.Children)
        {
            if (child is not FrameworkElement fe || fe.Visibility == Visibility.Collapsed) continue;
            if (fe == target) { x += fe.Margin.Left; break; }
            x += fe.ActualWidth + fe.Margin.Left + fe.Margin.Right;
        }
        var width = target.ActualWidth;
        var fresh = TabIndicator.Alpha.Value <= 0.002;
        if (instant || fresh)
        {
            TabIndicator.X.Snap(x);
            TabIndicator.PillWidth.Snap(width);
        }
        else
        {
            // position may spring; width never overshoots (a pill wider than its label looked "stretched")
            Go(TabIndicator.X, x, Motion.IsRefreshed ? S(0.26, 0.80) : Tk(MotionToken.SpatialFast));
            Go(TabIndicator.PillWidth, width, Tk(MotionToken.Data));
        }
        Go(TabIndicator.Alpha, 1, Tk(MotionToken.Fx), 0, instant);
    }

    /// <summary>Screenshot helper: waits until layout/rendering has settled.</summary>
    internal Task SettleAsync(int ms) =>
        Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task.ContinueWith(_ => Task.Delay(ms)).Unwrap();
}
