using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.UI.Animation;
using NotchHub.UI.Theming.Mcu;

namespace NotchHub.UI.Theming;

/// <summary>
/// Dynamic color: derives the source color (wallpaper dominant / wallpaper average / Windows accent / custom), generates a
/// Material 3 dark scheme for the configured variant and applies it to every Brush.* / Color.* token in
/// Application.Current.Resources (animated). See docs/DESIGN.md §1.
/// </summary>
public sealed class ThemeService : IBackgroundService
{
    private static readonly Color Fallback = Color.FromRgb(0xD4, 0xAF, 0x5A);
    private static readonly Dictionary<string, SolidColorBrush> InstalledBrushes = new(StringComparer.Ordinal);

    private readonly ISettingsService _settings;
    private readonly IWallpaperService _wallpaper;
    private readonly object _gate = new();
    private IReadOnlyList<Color> _candidates = [];
    private Color? _average;
    private bool _wallpaperDirty = true;
    private string _appearanceKey = "";
    private int _version;
    private bool _started;
    private EventHandler? _changed;

    public ThemeService(ISettingsService settings, IWallpaperService wallpaper)
    {
        _settings = settings;
        _wallpaper = wallpaper;
        Current = this;
    }

    /// <summary>The running instance (set by the constructor) — used by the settings UI.</summary>
    public static ThemeService? Current { get; private set; }

    public Color SourceColor { get; private set; } = Fallback;

    /// <summary>Up to 4 ranked candidate source colors extracted from the wallpaper (Material You style).</summary>
    public IReadOnlyList<Color> WallpaperCandidates { get { lock (_gate) return _candidates; } }

    /// <summary>Average wallpaper color ("as if blurred to one color"); null when unknown.</summary>
    public Color? AverageColor { get { lock (_gate) return _average; } }

    /// <summary>The token map (token name without prefix → color) currently applied.</summary>
    public IReadOnlyDictionary<string, Color>? CurrentScheme { get; private set; }

    /// <summary>Raised (UI thread) after a new scheme has been applied.</summary>
    public event EventHandler? Changed { add => _changed += value; remove => _changed -= value; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        _appearanceKey = AppearanceKey(_settings.Current.Appearance);
        _settings.Changed += OnSettingsChanged;
        _wallpaper.Changed += OnWallpaperChanged;
        return RefreshAsync();
    }

    /// <summary>Re-reads the wallpaper / settings and re-applies the scheme.</summary>
    public async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _version);
        try
        {
            var a = _settings.Current.Appearance;
            var source = Fallback;
            switch (a.Source)
            {
                case ColorSource.WallpaperDominant:
                case ColorSource.WallpaperAverage:
                    await EnsureWallpaperColorsAsync().ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (a.Source == ColorSource.WallpaperAverage) source = _average ?? Fallback;
                        else if (_candidates.Count > 0) source = _candidates[Math.Clamp(a.WallpaperCandidateIndex, 0, _candidates.Count - 1)];
                    }
                    break;
                case ColorSource.WindowsAccent:
                    source = AccentColor() ?? Fallback;
                    break;
                case ColorSource.Custom:
                    source = ParseHex(a.CustomColor) ?? Fallback;
                    break;
            }
            var variant = a.Variant;
            var contrast = a.Contrast;
            var panel = a.PanelStyle;
            var map = await Task.Run(() => Generate(source, variant, contrast, panel)).ConfigureAwait(false);
            if (version != Volatile.Read(ref _version)) return;   // a newer refresh is running

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            await dispatcher.InvokeAsync(() =>
            {
                if (version != _version) return;
                SourceColor = source;
                CurrentScheme = map;
                Apply(map, animate: true);
                _changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            Log.Warn("ThemeService: refresh failed", ex);
        }
    }

    public void Dispose()
    {
        if (!_started) return;
        _settings.Changed -= OnSettingsChanged;
        _wallpaper.Changed -= OnWallpaperChanged;
        _started = false;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var key = AppearanceKey(_settings.Current.Appearance);
        if (key == _appearanceKey) return;
        _appearanceKey = key;
        RefreshAsync().Forget("ThemeService.settings");
    }

    private void OnWallpaperChanged(object? sender, EventArgs e)
    {
        lock (_gate) _wallpaperDirty = true;
        var src = _settings.Current.Appearance.Source;
        // candidates are also shown in settings, so recompute even for non-wallpaper sources
        if (src is ColorSource.WallpaperDominant or ColorSource.WallpaperAverage) RefreshAsync().Forget("ThemeService.wallpaper");
        else EnsureWallpaperColorsAsync().Forget("ThemeService.wallpaper");
    }

    private static string AppearanceKey(AppearanceSettings a) => string.Join('|',
        a.Source, a.WallpaperCandidateIndex, a.CustomColor, a.Variant, a.PanelStyle, a.Contrast.ToString(CultureInfo.InvariantCulture));

    private async Task EnsureWallpaperColorsAsync()
    {
        lock (_gate) if (!_wallpaperDirty) return;
        List<Color> candidates = [];
        Color? average = null;
        try
        {
            if (_wallpaper.SolidColor is uint solid)
            {
                var c = FromArgb((int)(solid | 0xFF000000));
                candidates.Add(c);
                average = c;
            }
            else if (await _wallpaper.GetPixelsAsync(128).ConfigureAwait(false) is { } px && px.Argb.Length > 0)
            {
                await Task.Run(() =>
                {
                    var ranked = ColorExtraction.Score(ColorExtraction.Quantize(px.Argb), 4, ToArgb(Fallback));
                    candidates.AddRange(ranked.Select(FromArgb));
                    average = FromArgb(ColorExtraction.Average(px.Argb));
                }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("ThemeService: wallpaper colors unavailable", ex);
        }
        lock (_gate)
        {
            _candidates = candidates;
            _average = average;
            _wallpaperDirty = false;
        }
    }

    // ───────────────────────────── scheme generation ─────────────────────────────

    private readonly record struct Palette(double Hue, double Chroma)
    {
        public Color Tone(double tone) => FromArgb(Hct.Solve(Hue, Chroma, Math.Clamp(tone, 0, 100)));
    }

    private static double Rotate(double sourceHue, double[] hues, double[] rotations)
    {
        for (var i = 0; i < hues.Length - 1; i++)
            if (sourceHue >= hues[i] && sourceHue < hues[i + 1])
                return MathUtils.SanitizeDegreesDouble(sourceHue + rotations[i]);
        return sourceHue;
    }

    private static readonly double[] RotHues = [0, 41, 61, 101, 131, 181, 251, 301, 360];

    /// <summary>
    /// Material 3 dark scheme mapped to the NotchHub tokens (DESIGN.md §1). Keys are token names without the
    /// "Brush." / "Color." prefix (e.g. "Primary", "Surface2", "Gauge.Cpu").
    /// </summary>
    public static IReadOnlyDictionary<string, Color> Generate(Color source, SchemeVariant variant, double contrast, PanelStyle panelStyle)
    {
        var src = Hct.FromInt(ToArgb(source));
        double h = src.Hue, c = src.Chroma;
        Palette p, s, t, n, nv;
        switch (variant)
        {
            case SchemeVariant.Vibrant:
                p = new(h, 200);
                s = new(Rotate(h, RotHues, [18, 15, 10, 12, 15, 18, 15, 12, 12]), 24);
                t = new(Rotate(h, RotHues, [35, 30, 20, 25, 30, 35, 30, 25, 25]), 32);
                n = new(h, 10); nv = new(h, 12);
                break;
            case SchemeVariant.Expressive:
                p = new(h + 240, 40);
                s = new(Rotate(h, RotHues, [45, 95, 45, 20, 45, 90, 45, 45, 45]), 24);
                t = new(Rotate(h, RotHues, [120, 120, 20, 45, 20, 15, 20, 120, 120]), 32);
                n = new(h + 15, 8); nv = new(h + 15, 12);
                break;
            case SchemeVariant.Fidelity:
            case SchemeVariant.Content:
                p = new(h, c);
                s = new(h, Math.Max(c - 32, c * 0.5));
                t = new(h + (variant == SchemeVariant.Fidelity ? 180 : 60), Math.Max(24, c * 0.6));
                n = new(h, c / 8); nv = new(h, c / 8 + 4);
                break;
            case SchemeVariant.Neutral:
                p = new(h, 12); s = new(h, 8); t = new(h + 60, 16); n = new(h, 2); nv = new(h, 2);
                break;
            case SchemeVariant.Monochrome:
                p = new(h, 0); s = new(h, 0); t = new(h, 0); n = new(h, 0); nv = new(h, 0);
                break;
            case SchemeVariant.Rainbow:
                p = new(h, 48); s = new(h, 16); t = new(h + 60, 24); n = new(h, 0); nv = new(h, 0);
                break;
            case SchemeVariant.FruitSalad:
                p = new(h - 50, 48); s = new(h - 50, 36); t = new(h, 36); n = new(h, 10); nv = new(h, 16);
                break;
            default: // TonalSpot
                p = new(h, 36); s = new(h, 16); t = new(h + 60, 24); n = new(h, 6); nv = new(h, 8);
                break;
        }
        var e = new Palette(25, 84);
        var k = Math.Clamp(contrast, -1, 1);
        double Fg(double tone, double delta) => tone + delta * k;      // higher contrast → brighter foregrounds
        double Bg(double tone, double delta) => tone - delta * k;      // and darker containers

        var m = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["Panel"] = panelStyle == PanelStyle.Tinted ? n.Tone(4) : Colors.Black,
            ["Surface1"] = n.Tone(10),
            ["Surface2"] = n.Tone(15),
            ["Surface3"] = n.Tone(20),
            ["Surface4"] = n.Tone(25),
            ["OnSurface"] = n.Tone(Fg(90, 8)),
            ["OnSurfaceVariant"] = nv.Tone(Fg(80, 10)),
            ["OnSurfaceMuted"] = nv.Tone(Fg(60, 15)),
            ["Outline"] = nv.Tone(Fg(50, 15)),
            ["OutlineVariant"] = nv.Tone(Fg(25, 8)),
            ["Primary"] = p.Tone(Fg(80, 8)),
            ["OnPrimary"] = p.Tone(Bg(20, 10)),
            ["PrimaryContainer"] = p.Tone(Bg(30, 6)),
            ["OnPrimaryContainer"] = p.Tone(Fg(90, 8)),
            ["PrimarySoft"] = p.Tone(Fg(90, 5)),
            ["OnPrimarySoft"] = p.Tone(Bg(10, 5)),
            ["Secondary"] = s.Tone(Fg(80, 8)),
            ["OnSecondary"] = s.Tone(Bg(20, 10)),
            ["SecondaryContainer"] = s.Tone(Bg(30, 6)),
            ["OnSecondaryContainer"] = s.Tone(Fg(90, 8)),
            ["Tertiary"] = t.Tone(Fg(80, 8)),
            ["OnTertiary"] = t.Tone(Bg(20, 10)),
            ["TertiaryContainer"] = t.Tone(Bg(30, 6)),
            ["OnTertiaryContainer"] = t.Tone(Fg(90, 8)),
            ["Error"] = e.Tone(Fg(80, 8)),
            ["OnError"] = e.Tone(Bg(20, 10)),
            ["ErrorContainer"] = e.Tone(Bg(30, 6)),
            ["OnErrorContainer"] = e.Tone(Fg(90, 8)),
        };
        Color Harmonized(uint argb, double tone, double chroma) =>
            variant == SchemeVariant.Monochrome
                ? FromArgb(ColorUtils.ArgbFromLstar(tone))
                : FromArgb(Hct.Harmonize(Hct.FromInt(unchecked((int)argb)), src, tone, chroma).Argb);
        m["Success"] = Harmonized(0xFF4CAF50, Fg(80, 8), 40);
        m["OnSuccess"] = Harmonized(0xFF4CAF50, Bg(20, 10), 40);
        m["Warning"] = Harmonized(0xFFF3B33D, Fg(80, 8), 52);
        m["Info"] = Harmonized(0xFF2196F3, Fg(80, 8), 40);
        m["Pink"] = Harmonized(0xFFF06292, Fg(75, 8), 48);
        m["Gauge.Cpu"] = m["Primary"];
        m["Gauge.Gpu"] = m["Tertiary"];
        m["Gauge.Ram"] = m["Success"];
        var on = m["OnSurface"];
        m["StateHover"] = Color.FromArgb(0x14, on.R, on.G, on.B);
        m["StatePressed"] = Color.FromArgb(0x1F, on.R, on.G, on.B);
        m["Scrim"] = Color.FromArgb(0x99, 0, 0, 0);
        return m;
    }

    /// <summary>
    /// Installs (once) a live SolidColorBrush per "Brush.{key}" in Application.Current.Resources and animates it to the map's
    /// colors (~300 ms; instant with Motion.ReduceMotion); also replaces the "Color.{key}" values. UI thread only.
    /// </summary>
    private static readonly Dictionary<string, Color> AppliedTargets = new(StringComparer.Ordinal);

    public static void Apply(IReadOnlyDictionary<string, Color> map, bool animate = true)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;
        var duration = new Duration(TimeSpan.FromMilliseconds(animate && !Motion.ReduceMotion ? 300 : 1));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        ease.Freeze();
        foreach (var (key, color) in map)
        {
            var colorKey = "Color." + key;
            if (res[colorKey] is not Color old || old != color) res[colorKey] = color;

            // A themed brush must stay mutable forever: WPF freezes freezables stored in resource dictionaries as soon as
            // they *can* be frozen, so the brush always keeps a held animation clock (never cleared). If it somehow got
            // frozen anyway, a fresh brush replaces it — DynamicResource consumers pick the new instance up.
            if (!InstalledBrushes.TryGetValue(key, out var brush) || brush.IsFrozen)
            {
                var from = res["Brush." + key] is SolidColorBrush existing ? existing.Color : color;
                brush = new SolidColorBrush(from);
                brush.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(from, from, TimeSpan.FromMilliseconds(1)) { FillBehavior = FillBehavior.HoldEnd });
                InstalledBrushes[key] = brush;
                res["Brush." + key] = brush;
                AppliedTargets.Remove(key);
            }
            if (AppliedTargets.TryGetValue(key, out var target) && target == color) continue;
            AppliedTargets[key] = color;
            var anim = new ColorAnimation(color, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorization, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

    private static Color? AccentColor()
    {
        try
        {
            if (DwmGetColorizationColor(out var argb, out _) == 0) return FromArgb((int)(argb | 0xFF000000));
        }
        catch (Exception ex)
        {
            Log.Warn("ThemeService: accent color unavailable", ex);
        }
        return null;
    }

    private static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        return s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? FromArgb((int)(v | 0xFF000000))
            : null;
    }

    private static Color FromArgb(int argb) =>
        Color.FromArgb(255, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static int ToArgb(Color c) => unchecked((int)0xFF000000) | c.R << 16 | c.G << 8 | c.B;
}
