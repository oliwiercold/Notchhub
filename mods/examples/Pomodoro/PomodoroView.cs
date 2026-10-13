using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NotchHub.UI;
using NotchHub.UI.Controls;

namespace Pomodoro;

/// <summary>
/// The Pomodoro tab, built in code (a mod can also use compiled XAML: add a UserControl .xaml to the project — UseWPF is on).
/// Uses the app's design system: WavyRing, M3Button, IconButton, FilterChip, Card and Text.* styles, Brush.* tokens.
/// Implements ITabView so it listens to the timer only while visible.
/// </summary>
internal sealed class PomodoroView : UserControl, ITabView
{
    private static readonly (int Work, int Break)[] Presets = [(25, 5), (50, 10), (15, 3)];

    private readonly PomodoroMod _mod;
    private readonly WavyRing _ring;
    private readonly TextBlock _time;
    private readonly TextBlock _phase;
    private readonly TextBlock _completed;
    private readonly M3Button _startPause;
    private readonly List<(ToggleButton Chip, int Work, int Break)> _chips = [];

    public PomodoroView(PomodoroMod mod)
    {
        _mod = mod;
        SetResourceReference(ForegroundProperty, "Brush.OnSurface");

        // ── left: ring with the countdown ──
        _time = Text("Text.Display");
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        _phase = Text("Text.Label");
        _phase.HorizontalAlignment = HorizontalAlignment.Center;
        var ringContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        ringContent.Children.Add(_phase);
        ringContent.Children.Add(_time);
        _ring = new WavyRing { Width = 230, Height = 230, Thickness = 14, Content = ringContent, Margin = new Thickness(8, 0, 28, 0) };

        // ── right: title, presets, buttons ──
        var title = Text("Text.Headline", "Pomodoro");
        var desc = Text("Text.BodyS", PomodoroMod.T(
            "Skup się przez jedną sesję, potem krótka przerwa. Powiadomienie pojawi się w notchu.",
            "Focus for one session, then take a short break. You'll get a heads-up in the notch."));
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 4, 0, 14);

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        foreach (var (w, b) in Presets)
        {
            var chip = new ToggleButton { Content = $"{w} / {b} min", Margin = new Thickness(0, 0, 8, 0) };
            chip.SetResourceReference(StyleProperty, "FilterChip");
            chip.Click += (_, _) => _mod.SetDurations(w, b);
            _chips.Add((chip, w, b));
            chips.Children.Add(chip);
        }

        _startPause = new M3Button { Variant = ButtonVariant.Filled, Size = ButtonSize.L, MinWidth = 150 };
        _startPause.Click += (_, _) => _mod.Toggle();
        var reset = new M3Button
        {
            Variant = ButtonVariant.Tonal, Size = ButtonSize.L, Icon = "replay", Margin = new Thickness(10, 0, 0, 0),
            Content = PomodoroMod.T("Od nowa", "Reset"),
        };
        reset.Click += (_, _) => _mod.Reset();
        var skip = new IconButton { Icon = "skip_next", Variant = IconButtonVariant.Outlined, Size = 52, Margin = new Thickness(10, 0, 0, 0) };
        skip.ToolTip = PomodoroMod.T("Pomiń fazę", "Skip phase");
        skip.Click += (_, _) => _mod.Skip();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_startPause);
        buttons.Children.Add(reset);
        buttons.Children.Add(skip);

        _completed = Text("Text.Title");
        var stats = new Border { Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Left };
        stats.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
        var statsRow = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Icon { Glyph = "local_fire_department", Size = 20, Filled = true, Margin = new Thickness(0, 0, 10, 0) };
        icon.SetResourceReference(ForegroundProperty, "Brush.Tertiary");
        statsRow.Children.Add(icon);
        statsRow.Children.Add(_completed);
        stats.Child = statsRow;

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(title);
        right.Children.Add(desc);
        right.Children.Add(chips);
        right.Children.Add(buttons);
        right.Children.Add(stats);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(_ring);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var card = new Border { Padding = new Thickness(20) };
        card.SetResourceReference(StyleProperty, "Card");
        card.Child = grid;
        Content = card;
        Refresh();
    }

    public void OnShown()
    {
        _mod.Changed -= OnChanged;
        _mod.Changed += OnChanged;
        Refresh();
    }

    public void OnHidden() => _mod.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var work = _mod.Phase == Phase.Work;
        _time.Text = _mod.RemainingText;
        _phase.Text = work ? PomodoroMod.T("PRACA", "FOCUS") : PomodoroMod.T("PRZERWA", "BREAK");
        _ring.Value = _mod.Fraction;
        _ring.IsWavy = _mod.IsRunning;
        _ring.SetResourceReference(WavyRing.ActiveBrushProperty, work ? "Brush.Primary" : "Brush.Tertiary");
        _startPause.Icon = _mod.IsRunning ? "pause" : "play_arrow";
        _startPause.Content = _mod.IsRunning ? PomodoroMod.T("Pauza", "Pause") : PomodoroMod.T("Start", "Start");
        _completed.Text = PomodoroMod.T($"Ukończone dziś: {_mod.CompletedToday}", $"Completed today: {_mod.CompletedToday}");
        foreach (var (chip, w, b) in _chips) chip.IsChecked = w == _mod.WorkMinutes && b == _mod.BreakMinutes;
    }

    private static TextBlock Text(string style, string? text = null)
    {
        var t = new TextBlock { Text = text ?? "" };
        t.SetResourceReference(StyleProperty, style);
        return t;
    }
}
