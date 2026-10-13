using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Settings;
using NotchHub.UI.Localization;
using NotchHub.UI.Theming;

namespace NotchHub.Features.Settings;

/// <summary>Settings tab. Every change is applied live through AppServices.Settings.Update (sliders debounced).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] Presets =
        ["#D4AF5A", "#E8806A", "#E86A9A", "#B07AE0", "#6F8CF0", "#4FB8E8", "#4DB6A0", "#7CC46A", "#E8B04A"];

    private bool _loading;
    private bool _selfUpdate;
    private bool _shown;
    private DispatcherTimer? _debounce;
    private readonly Dictionary<string, Action<AppSettings>> _pending = new();

    private static ISettingsService Settings => AppServices.Settings;
    private static AppSettings S => AppServices.Settings.Current;

    public SettingsViewModel()
    {
        Load();
    }

    // ───────────── lifecycle ─────────────

    public void OnShown()
    {
        if (_shown) return;
        _shown = true;
        Settings.Changed += OnSettingsChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        if (ThemeService.Current is { } t) t.Changed += OnThemeChanged;
        if (AppServices.Phone is { } p) p.Changed += OnPhoneChanged;
        if (AppServices.Calendar is { } c) c.Changed += OnCalendarChanged;
        if (AppServices.Media is { } media) media.Changed += OnMediaChanged;
        Load();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        Flush();
        Settings.Changed -= OnSettingsChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
        if (ThemeService.Current is { } t) t.Changed -= OnThemeChanged;
        if (AppServices.Phone is { } p) p.Changed -= OnPhoneChanged;
        if (AppServices.Calendar is { } c) c.Changed -= OnCalendarChanged;
        if (AppServices.Media is { } media) media.Changed -= OnMediaChanged;
        CancelCapture();
        ClearConfirm = false;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_selfUpdate) return;
        AppServices.Ui.Post(Load);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => AppServices.Ui.Post(Load);
    private void OnThemeChanged(object? sender, EventArgs e) => AppServices.Ui.Post(LoadAppearanceSwatches);
    private void OnPhoneChanged(object? sender, EventArgs e) => AppServices.Ui.Post(LoadPhoneState);
    private void OnCalendarChanged(object? sender, EventArgs e) => AppServices.Ui.Post(() =>
    {
        LoadCalendarStatus();
        LoadMeetingCalendars();
    });

    // ───────────── navigation ─────────────

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SectionIndex))] private SettingsSection _section = SettingsSection.Appearance;

    public int SectionIndex
    {
        get => (int)Section;
        set { if (value >= 0) Section = (SettingsSection)value; }
    }

    // ───────────── persistence helpers ─────────────

    private void Save(Action<AppSettings> mutate)
    {
        if (_loading) return;
        _selfUpdate = true;
        try { Settings.Update(mutate); }
        catch (Exception ex) { Log.Warn("Settings: update failed", ex); }
        finally { _selfUpdate = false; }
    }

    /// <summary>Coalesces rapid changes (slider drags) into one save ~250 ms later.</summary>
    private void Defer(string key, Action<AppSettings> mutate)
    {
        if (_loading) return;
        _pending[key] = mutate;
        if (_debounce is null)
        {
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += (_, _) => Flush();
        }
        _debounce.Stop();
        _debounce.Start();
    }

    private void Flush()
    {
        _debounce?.Stop();
        if (_pending.Count == 0) return;
        var all = _pending.Values.ToList();
        _pending.Clear();
        Save(s => { foreach (var a in all) a(s); });
    }

    private void Load()
    {
        _loading = true;
        try
        {
            LoadAppearance();
            LoadNotch();
            LoadShortcuts();
            LoadClipboard();
            LoadWeather();
            LoadCalendar();
            LoadPhone();
            LoadAnimationStyles();
            LoadMods();
            LoadExtras();
            LoadGeneral();
            LoadSpotify();
        }
        catch (Exception ex) { Log.Warn("Settings: load failed", ex); }
        finally { _loading = false; }
    }

    // ═════════════ Wygląd ═════════════

    public ObservableCollection<Swatch> SourceSwatches { get; } = [];
    public ObservableCollection<Swatch> VariantSwatches { get; } = [];
    public ObservableCollection<ColorDot> PresetColors { get; } = [];
    [ObservableProperty] private ObservableCollection<Choice> _panelStyles = [];
    [ObservableProperty] private string _customHex = "";
    [ObservableProperty] private bool _customHexInvalid;
    [ObservableProperty] private bool _isCustomSource;
    [ObservableProperty] private double _contrast;
    [ObservableProperty] private bool _reduceMotion;

    public string ContrastText => Contrast switch
    {
        < -0.33 => Loc.Get("settings.Appearance.ContrastLow"),
        > 0.66 => Loc.Get("settings.Appearance.ContrastHigh"),
        > 0.2 => Loc.Get("settings.Appearance.ContrastMedium"),
        _ => Loc.Get("settings.Appearance.ContrastStandard"),
    };

    private void LoadAppearance()
    {
        var a = S.Appearance;
        CustomHex = a.CustomColor;
        CustomHexInvalid = false;
        IsCustomSource = a.Source == ColorSource.Custom;
        Contrast = a.Contrast;
        ReduceMotion = a.ReduceMotion;
        PanelStyles = Choices.Build(
            [(Loc.Get("settings.Appearance.PanelBlack"), PanelStyle.Black), (Loc.Get("settings.Appearance.PanelTinted"), PanelStyle.Tinted)],
            a.PanelStyle, v => Save(s => s.Appearance.PanelStyle = (PanelStyle)v));
        if (PresetColors.Count == 0)
            foreach (var hex in Presets) PresetColors.Add(new ColorDot(hex, PickPreset));
        foreach (var d in PresetColors) d.IsSelected = string.Equals(d.Hex, a.CustomColor, StringComparison.OrdinalIgnoreCase);
        LoadAppearanceSwatches();
    }

    private void LoadAppearanceSwatches()
    {
        var a = S.Appearance;
        var theme = ThemeService.Current;
        SourceSwatches.Clear();
        var candidates = theme?.WallpaperCandidates ?? [];
        for (var i = 0; i < Math.Min(4, candidates.Count); i++)
        {
            var label = i == 0 ? Loc.Get("settings.Appearance.Wallpaper") : Loc.Format("settings.Appearance.WallpaperN", i + 1);
            SourceSwatches.Add(MakeSwatch(label, (ColorSource.WallpaperDominant, i), candidates[i], a.Variant, PickSource));
        }
        if (theme?.AverageColor is { } avg)
            SourceSwatches.Add(MakeSwatch(Loc.Get("settings.Appearance.Average"), (ColorSource.WallpaperAverage, 0), avg, a.Variant, PickSource));
        SourceSwatches.Add(MakeSwatch(Loc.Get("settings.Appearance.Accent"), (ColorSource.WindowsAccent, 0), ColorUtil.WindowsAccent(), a.Variant, PickSource));
        var custom = ColorUtil.Parse(a.CustomColor) ?? Colors.Goldenrod;
        SourceSwatches.Add(MakeSwatch(Loc.Get("settings.Appearance.Custom"), (ColorSource.Custom, 0), custom, a.Variant, PickSource));
        SyncSourceSelection();

        var source = theme?.SourceColor ?? custom;
        VariantSwatches.Clear();
        foreach (var v in Enum.GetValues<SchemeVariant>())
        {
            var sw = MakeSwatch(Loc.Get("settings.Appearance.Variant." + v), v, source, v, PickVariant);
            sw.IsSelected = v == a.Variant;
            VariantSwatches.Add(sw);
        }
    }

    private static Swatch MakeSwatch(string label, object key, Color source, SchemeVariant variant, Action<Swatch> pick)
    {
        Brush pa, pb, pc;
        try
        {
            var map = ThemeService.Generate(source, variant, S.Appearance.Contrast, PanelStyle.Black);
            Color Get(string k) => map.TryGetValue(k, out var c) ? c : source;
            pa = ColorUtil.BrushOf(Get("Primary"));
            pb = ColorUtil.BrushOf(Get("SecondaryContainer"));
            pc = ColorUtil.BrushOf(Get("Tertiary"));
        }
        catch (Exception ex)
        {
            Log.Warn("Settings: scheme preview failed", ex);
            pa = pb = pc = ColorUtil.BrushOf(source);
        }
        return new Swatch(label, key, source, pa, pb, pc, pick);
    }

    private void SyncSourceSelection()
    {
        var a = S.Appearance;
        var sel = SourceSwatches.FirstOrDefault(sw => sw.Key is ValueTuple<ColorSource, int> k && k.Item1 == a.Source
                                                      && (a.Source != ColorSource.WallpaperDominant || k.Item2 == a.WallpaperCandidateIndex))
                  ?? SourceSwatches.FirstOrDefault(sw => sw.Key is ValueTuple<ColorSource, int> k && k.Item1 == a.Source);
        foreach (var sw in SourceSwatches) sw.Sync(ReferenceEquals(sw, sel));
    }

    private void PickSource(Swatch sw)
    {
        var (src, idx) = (ValueTuple<ColorSource, int>)sw.Key;
        foreach (var x in SourceSwatches) x.Sync(ReferenceEquals(x, sw));
        IsCustomSource = src == ColorSource.Custom;
        Save(s =>
        {
            s.Appearance.Source = src;
            if (src == ColorSource.WallpaperDominant) s.Appearance.WallpaperCandidateIndex = idx;
        });
    }

    private void PickVariant(Swatch sw)
    {
        foreach (var x in VariantSwatches) x.Sync(ReferenceEquals(x, sw));
        Save(s => s.Appearance.Variant = (SchemeVariant)sw.Key);
    }

    private void PickPreset(ColorDot dot)
    {
        CustomHex = dot.Hex;
        ApplyCustomHex();
    }

    [RelayCommand]
    private void ApplyCustomHex()
    {
        var c = ColorUtil.Parse(CustomHex);
        CustomHexInvalid = c is null;
        if (c is null) return;
        var hex = ColorUtil.ToHex(c.Value);
        foreach (var d in PresetColors) d.IsSelected = string.Equals(d.Hex, hex, StringComparison.OrdinalIgnoreCase);
        IsCustomSource = true;
        Save(s =>
        {
            s.Appearance.CustomColor = hex;
            s.Appearance.Source = ColorSource.Custom;
        });
        // custom swatch preview follows the new color immediately (theme Changed refreshes the rest)
        var idx = SourceSwatches.ToList().FindIndex(x => x.Key is ValueTuple<ColorSource, int> k && k.Item1 == ColorSource.Custom);
        if (idx >= 0)
        {
            SourceSwatches[idx] = MakeSwatch(Loc.Get("settings.Appearance.Custom"), (ColorSource.Custom, 0), c.Value, S.Appearance.Variant, PickSource);
            SyncSourceSelection();
        }
    }

    partial void OnCustomHexChanged(string value)
    {
        if (!_loading && ColorUtil.Parse(value) is not null && value.Trim().TrimStart('#').Length == 6) ApplyCustomHex();
    }

    partial void OnContrastChanged(double value)
    {
        OnPropertyChanged(nameof(ContrastText));
        Defer("contrast", s => s.Appearance.Contrast = Math.Round(value, 2));
    }

    partial void OnReduceMotionChanged(bool value) => Save(s => s.Appearance.ReduceMotion = value);

    // ═════════════ Notch ═════════════

    [ObservableProperty] private bool _reserveScreenSpace;
    [ObservableProperty] private ObservableCollection<Choice> _reservedSpaceModes = [];
    [ObservableProperty] private ObservableCollection<Choice> _monitorChoices = [];
    [ObservableProperty] private ObservableCollection<Choice> _hoverChoices = [];
    [ObservableProperty] private ObservableCollection<Choice> _tabChoices = [];
    [ObservableProperty] private double _collapsedWidth;
    [ObservableProperty] private double _collapsedHeight;
    [ObservableProperty] private double _hoverDelay;
    [ObservableProperty] private double _collapseDelay;
    [ObservableProperty] private bool _showLiveActivity;
    [ObservableProperty] private bool _showHud;
    [ObservableProperty] private bool _scrollToExpand;
    [ObservableProperty] private bool _hideInFullscreen;
    [ObservableProperty] private bool _rememberLastTab;

    public string CollapsedWidthText => $"{CollapsedWidth:0} px";
    public string CollapsedHeightText => $"{CollapsedHeight:0} px";
    public string HoverDelayText => $"{HoverDelay:0} ms";
    public string CollapseDelayText => $"{CollapseDelay:0} ms";

    private void LoadNotch()
    {
        var n = S.Notch;
        ReserveScreenSpace = n.ReserveScreenSpace;
        ReservedSpaceModes = Choices.Build(
            [(Loc.Get("settings.Notch.ReserveNotch"), ReservedSpaceMode.Notch), (Loc.Get("settings.Notch.ReserveActivity"), ReservedSpaceMode.LiveActivity),
             (Loc.Get("settings.Notch.ReserveDynamic"), ReservedSpaceMode.Dynamic)],
            n.ReservedSpace, v => Save(s => s.Notch.ReservedSpace = (ReservedSpaceMode)v));

        var monitors = Monitors.List();
        var mons = new List<(string, object)> { (Loc.Get("settings.Notch.MonitorPrimary"), -1) };
        for (var i = 0; i < monitors.Count; i++)
            mons.Add((Loc.Format("settings.Notch.MonitorN", i + 1, monitors[i].Width, monitors[i].Height), i));
        MonitorChoices = Choices.Build(mons, n.MonitorIndex, v => Save(s => s.Notch.MonitorIndex = (int)v));

        HoverChoices = Choices.Build(
        [
            (Loc.Get("settings.Notch.HoverPeek"), HoverBehavior.PeekThenClick),
            (Loc.Get("settings.Notch.HoverExpand"), HoverBehavior.ExpandOnHover),
            (Loc.Get("settings.Notch.HoverClick"), HoverBehavior.ClickOnly),
        ], n.Hover, v => Save(s => s.Notch.Hover = (HoverBehavior)v));

        string[] tabs = ["Controls", "Today", "Stats", "Clipboard", "Phone", "Power"];
        TabChoices = Choices.Build(tabs.Select(t => (Loc.Get("settings.Tab." + t), (object)t)), n.DefaultTab,
            v => Save(s => s.Notch.DefaultTab = (string)v));

        CollapsedWidth = n.CollapsedWidth;
        CollapsedHeight = n.CollapsedHeight;
        HoverDelay = n.HoverDelayMs;
        CollapseDelay = n.CollapseDelayMs;
        ShowLiveActivity = n.ShowLiveActivity;
        ShowHud = n.ShowHud;
        ScrollToExpand = n.ScrollToExpand;
        HideInFullscreen = n.HideInFullscreen;
        RememberLastTab = n.RememberLastTab;
    }

    partial void OnReserveScreenSpaceChanged(bool value) => Save(s => s.Notch.ReserveScreenSpace = value);
    partial void OnShowLiveActivityChanged(bool value) => Save(s => s.Notch.ShowLiveActivity = value);
    partial void OnShowHudChanged(bool value) => Save(s => s.Notch.ShowHud = value);
    partial void OnScrollToExpandChanged(bool value) => Save(s => s.Notch.ScrollToExpand = value);
    partial void OnHideInFullscreenChanged(bool value) => Save(s => s.Notch.HideInFullscreen = value);
    partial void OnRememberLastTabChanged(bool value) => Save(s => s.Notch.RememberLastTab = value);

    partial void OnCollapsedWidthChanged(double value)
    {
        OnPropertyChanged(nameof(CollapsedWidthText));
        Defer("cw", s => s.Notch.CollapsedWidth = Math.Round(value));
    }

    partial void OnCollapsedHeightChanged(double value)
    {
        OnPropertyChanged(nameof(CollapsedHeightText));
        Defer("ch", s => s.Notch.CollapsedHeight = Math.Round(value));
    }

    partial void OnHoverDelayChanged(double value)
    {
        OnPropertyChanged(nameof(HoverDelayText));
        Defer("hd", s => s.Notch.HoverDelayMs = (int)Math.Round(value));
    }

    partial void OnCollapseDelayChanged(double value)
    {
        OnPropertyChanged(nameof(CollapseDelayText));
        Defer("cd", s => s.Notch.CollapseDelayMs = (int)Math.Round(value));
    }
}
