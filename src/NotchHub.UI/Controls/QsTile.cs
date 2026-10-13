using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotchHub.UI.Animation;

namespace NotchHub.UI.Controls;

/// <summary>
/// Quick-settings tile (height 64). Inactive: Surface2, radius 20. Active: Primary, full pill. Optional split chevron
/// segment (46 px, 2 px gap) that runs <see cref="ChevronCommand"/>. Both segments squish on press.
/// Clicking the main segment executes <see cref="Command"/>(<see cref="CommandParameter"/>); IsActive is NOT toggled
/// automatically — bind it to the view model state.
/// </summary>
public class QsTile : Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(QsTile), new FrameworkPropertyMetadata("", (d, _) => ((QsTile)d).UpdateIcon()));
    public static readonly DependencyProperty ActiveIconProperty = DependencyProperty.Register(
        nameof(ActiveIcon), typeof(string), typeof(QsTile), new FrameworkPropertyMetadata("", (d, _) => ((QsTile)d).UpdateIcon()));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(QsTile), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(QsTile), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(QsTile), new FrameworkPropertyMetadata(false, (d, _) => ((QsTile)d).OnActiveChanged()));
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(QsTile), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty ShowChevronProperty = DependencyProperty.Register(
        nameof(ShowChevron), typeof(bool), typeof(QsTile), new FrameworkPropertyMetadata(false, (d, _) => ((QsTile)d).UpdateShape(false)));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(QsTile));
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(QsTile));
    public static readonly DependencyProperty ChevronCommandProperty = DependencyProperty.Register(
        nameof(ChevronCommand), typeof(ICommand), typeof(QsTile));

    private static readonly DependencyPropertyKey CurrentIconKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentIcon), typeof(string), typeof(QsTile), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty CurrentIconProperty = CurrentIconKey.DependencyProperty;

    private static readonly DependencyPropertyKey MainCornerKey = DependencyProperty.RegisterReadOnly(
        nameof(MainCorner), typeof(CornerRadius), typeof(QsTile), new FrameworkPropertyMetadata(new CornerRadius(20)));
    public static readonly DependencyProperty MainCornerProperty = MainCornerKey.DependencyProperty;
    private static readonly DependencyPropertyKey ChevronCornerKey = DependencyProperty.RegisterReadOnly(
        nameof(ChevronCorner), typeof(CornerRadius), typeof(QsTile), new FrameworkPropertyMetadata(new CornerRadius(20)));
    public static readonly DependencyProperty ChevronCornerProperty = ChevronCornerKey.DependencyProperty;

    /// <summary>Animated 0..1 cross-fade between the inactive and the active (Primary) fill.</summary>
    public static readonly DependencyProperty ActiveAmountProperty = DependencyProperty.Register(
        nameof(ActiveAmount), typeof(double), typeof(QsTile), new FrameworkPropertyMetadata(0.0));

    private static readonly DependencyProperty MainRadiusProperty = DependencyProperty.Register(
        "MainRadius", typeof(double), typeof(QsTile), new FrameworkPropertyMetadata(20.0, (d, _) => ((QsTile)d).UpdateCorners()));
    private static readonly DependencyProperty ChevronRadiusProperty = DependencyProperty.Register(
        "ChevronRadius", typeof(double), typeof(QsTile), new FrameworkPropertyMetadata(20.0, (d, _) => ((QsTile)d).UpdateCorners()));

    private const double InnerRadius = 6, PressedRadius = 12, InactiveRadius = 20;
    private FrameworkElement? _main, _chevron, _pressed;

    static QsTile()
    {
        FocusableProperty.OverrideMetadata(typeof(QsTile), new FrameworkPropertyMetadata(true));
    }

    public QsTile()
    {
        SizeChanged += (_, _) => UpdateShape(false);
    }

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    /// <summary>Icon shown while active (falls back to <see cref="Icon"/>).</summary>
    public string ActiveIcon { get => (string)GetValue(ActiveIconProperty); set => SetValue(ActiveIconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    /// <summary>Shows a small loading indicator instead of the icon (e.g. while the radio is switching).</summary>
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public bool ShowChevron { get => (bool)GetValue(ShowChevronProperty); set => SetValue(ShowChevronProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public ICommand? ChevronCommand { get => (ICommand?)GetValue(ChevronCommandProperty); set => SetValue(ChevronCommandProperty, value); }
    public string CurrentIcon => (string)GetValue(CurrentIconProperty);
    public CornerRadius MainCorner => (CornerRadius)GetValue(MainCornerProperty);
    public CornerRadius ChevronCorner => (CornerRadius)GetValue(ChevronCornerProperty);
    public double ActiveAmount { get => (double)GetValue(ActiveAmountProperty); set => SetValue(ActiveAmountProperty, value); }

    /// <summary>Raised when the main segment is clicked (in addition to <see cref="Command"/>).</summary>
    public event RoutedEventHandler? Click;

    /// <summary>Raised when the chevron segment is clicked (in addition to <see cref="ChevronCommand"/>).</summary>
    public event RoutedEventHandler? ChevronClick;

    public override void OnApplyTemplate()
    {
        if (_main is not null) Detach(_main);
        if (_chevron is not null) Detach(_chevron);
        base.OnApplyTemplate();
        _main = GetTemplateChild("PART_Main") as FrameworkElement;
        _chevron = GetTemplateChild("PART_Chevron") as FrameworkElement;
        if (_main is not null) Attach(_main);
        if (_chevron is not null) Attach(_chevron);
        UpdateIcon();
        ActiveAmount = IsActive ? 1 : 0;
        UpdateShape(false);
    }

    private void Attach(FrameworkElement part)
    {
        part.MouseLeftButtonDown += OnPartDown;
        part.MouseLeftButtonUp += OnPartUp;
        part.LostMouseCapture += OnPartLostCapture;
    }

    private void Detach(FrameworkElement part)
    {
        part.MouseLeftButtonDown -= OnPartDown;
        part.MouseLeftButtonUp -= OnPartUp;
        part.LostMouseCapture -= OnPartLostCapture;
    }

    private void OnPartDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        _pressed = (FrameworkElement)sender;
        _pressed.CaptureMouse();
        UpdateShape(true);
        e.Handled = true;
    }

    private void OnPartUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed != sender) return;
        var part = _pressed;
        var inside = part.InputHitTest(e.GetPosition(part)) is not null;
        part.ReleaseMouseCapture();   // → OnPartLostCapture resets the shape
        if (inside) Invoke(part == _chevron);
        e.Handled = true;
    }

    private void OnPartLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressed != sender) return;
        _pressed = null;
        UpdateShape(true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space) { Invoke(false); e.Handled = true; }
        else if (e.Key == Key.Right && ShowChevron) { Invoke(true); e.Handled = true; }
    }

    private void Invoke(bool chevron)
    {
        var cmd = chevron ? ChevronCommand : Command;
        if (cmd?.CanExecute(CommandParameter) == true) cmd.Execute(CommandParameter);
        (chevron ? ChevronClick : Click)?.Invoke(this, new RoutedEventArgs());
    }

    private void UpdateIcon() =>
        SetValue(CurrentIconKey, IsActive && !string.IsNullOrEmpty(ActiveIcon) ? ActiveIcon : Icon ?? "");

    private void OnActiveChanged()
    {
        UpdateIcon();
        if (IsLoaded && !Motion.ReduceMotion) this.Animate(ActiveAmountProperty, IsActive ? 1 : 0, MotionToken.EffectsDefault);
        else this.Set(ActiveAmountProperty, IsActive ? 1 : 0);
        UpdateShape(true);
    }

    private void UpdateShape(bool animate)
    {
        var full = Math.Max(InactiveRadius, ActualHeight / 2);
        var rest = IsActive ? full : InactiveRadius;
        bool mainDown = _pressed is not null && _pressed == _main, chevronDown = _pressed is not null && _pressed == _chevron;
        // Refreshed: press morphs on Fx (no bounce, M3), the active ⇄ pill morph springs (≈ 0.30 / 0.80)
        ShapeMorph.To(this, MainRadiusProperty, mainDown ? PressedRadius : rest, animate, mainDown ? MotionToken.Fx : MotionToken.SpatialDefault);
        ShapeMorph.To(this, ChevronRadiusProperty, chevronDown ? PressedRadius : rest, animate, chevronDown ? MotionToken.Fx : MotionToken.SpatialDefault);
        UpdateCorners();
    }

    private void UpdateCorners()
    {
        var m = Math.Max(0, (double)GetValue(MainRadiusProperty));
        var c = Math.Max(0, (double)GetValue(ChevronRadiusProperty));
        if (ShowChevron)
        {
            SetValue(MainCornerKey, new CornerRadius(m, InnerRadius, InnerRadius, m));
            SetValue(ChevronCornerKey, new CornerRadius(InnerRadius, c, c, InnerRadius));
        }
        else
        {
            SetValue(MainCornerKey, new CornerRadius(m));
        }
    }
}
