using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NotchHub.UI.Animation;

namespace NotchHub.UI.Controls;

public enum ButtonVariant { Filled, Tonal, Outlined, Text }

public enum ButtonSize { S, M, L }

public enum IconButtonVariant { Standard, Filled, Tonal, Outlined }

/// <summary>Shared "shape morph" plumbing: an animated double radius mirrored into a CornerRadius for templates.</summary>
internal static class ShapeMorph
{
    public static DependencyPropertyKey RegisterCorner<T>() => DependencyProperty.RegisterReadOnly(
        "Corner", typeof(CornerRadius), typeof(T), new FrameworkPropertyMetadata(new CornerRadius(20)));

    public static DependencyProperty RegisterRadius<T>(DependencyPropertyKey cornerKey) => DependencyProperty.Register(
        "Radius", typeof(double), typeof(T), new FrameworkPropertyMetadata(20.0,
            (d, e) => d.SetValue(cornerKey, new CornerRadius(Math.Max(0, (double)e.NewValue)))));

    /// <param name="refreshed">Refreshed-profile token (M3: the press morph never bounces → Fx).</param>
    public static void To(FrameworkElement el, DependencyProperty radius, double target, bool animate, MotionToken refreshed = MotionToken.Fx)
    {
        if (double.IsNaN(target) || target < 0) target = 0;
        if (animate && el.IsLoaded && !Motion.ReduceMotion) el.Animate(radius, target, Motion.Pick(MotionToken.SpatialFast, refreshed));
        else el.Set(radius, target);
    }
}

/// <summary>M3 Expressive button: pill that squishes to radius 12 on press. Variant Filled / Tonal / Outlined / Text, Size S / M / L.</summary>
public class M3Button : Button
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(ButtonVariant), typeof(M3Button), new FrameworkPropertyMetadata(ButtonVariant.Filled));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(M3Button), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(ButtonSize), typeof(M3Button), new FrameworkPropertyMetadata(ButtonSize.M));

    private static readonly DependencyPropertyKey CornerKey = ShapeMorph.RegisterCorner<M3Button>();
    public static readonly DependencyProperty CornerProperty = CornerKey.DependencyProperty;
    public static readonly DependencyProperty RadiusProperty = ShapeMorph.RegisterRadius<M3Button>(CornerKey);

    public M3Button()
    {
        SizeChanged += (_, _) => UpdateShape(false);
    }

    public ButtonVariant Variant { get => (ButtonVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    /// <summary>Optional leading Material Symbols icon.</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public ButtonSize Size { get => (ButtonSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public CornerRadius Corner => (CornerRadius)GetValue(CornerProperty);
    /// <summary>Animated corner radius (internal, drives <see cref="Corner"/>).</summary>
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        UpdateShape(true);
    }

    private void UpdateShape(bool animate) =>
        ShapeMorph.To(this, RadiusProperty, IsPressed ? (Size == ButtonSize.S ? 8 : 12) : ActualHeight / 2, animate);
}

/// <summary>Circular icon button (squishes on press). Variant Standard / Filled / Tonal / Outlined, Size 32 / 40 / 48.</summary>
public class IconButton : Button
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconButton), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(IconButtonVariant), typeof(IconButton), new FrameworkPropertyMetadata(IconButtonVariant.Standard));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconButton), new FrameworkPropertyMetadata(40.0, (d, _) => ((IconButton)d).OnSizeChangedInternal()));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(IconButton), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IconFilledProperty = DependencyProperty.Register(
        nameof(IconFilled), typeof(bool), typeof(IconButton), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey IconSizeKey = DependencyProperty.RegisterReadOnly(
        nameof(IconSize), typeof(double), typeof(IconButton), new FrameworkPropertyMetadata(22.0));
    public static readonly DependencyProperty IconSizeProperty = IconSizeKey.DependencyProperty;

    private static readonly DependencyPropertyKey CornerKey = ShapeMorph.RegisterCorner<IconButton>();
    public static readonly DependencyProperty CornerProperty = CornerKey.DependencyProperty;
    public static readonly DependencyProperty RadiusProperty = ShapeMorph.RegisterRadius<IconButton>(CornerKey);

    public IconButton() => OnSizeChangedInternal();

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public IconButtonVariant Variant { get => (IconButtonVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    /// <summary>Diameter in DIPs (32 / 40 / 48).</summary>
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    /// <summary>Toggle state (Standard: Primary icon; Tonal/Outlined: filled container). Not toggled automatically.</summary>
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IconFilled { get => (bool)GetValue(IconFilledProperty); set => SetValue(IconFilledProperty, value); }
    public double IconSize => (double)GetValue(IconSizeProperty);
    public CornerRadius Corner => (CornerRadius)GetValue(CornerProperty);
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    private void OnSizeChangedInternal()
    {
        SetValue(IconSizeKey, Math.Round(Math.Max(16, Size * 0.55)));
        UpdateShape(false);
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        UpdateShape(true);
    }

    private void UpdateShape(bool animate) => ShapeMorph.To(this, RadiusProperty, IsPressed ? Size * 0.3 : Size / 2, animate);
}

/// <summary>
/// Big play/pause toggle (IsChecked = playing). Shape morphs: paused = rounded square (30 %), playing = circle,
/// pressed = squished; icons cross-fade play_arrow ↔ pause.
/// </summary>
public class PlayPauseButton : ToggleButton
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(PlayPauseButton), new FrameworkPropertyMetadata(48.0, (d, _) => ((PlayPauseButton)d).OnSizeChangedInternal()));

    private static readonly DependencyPropertyKey IconSizeKey = DependencyProperty.RegisterReadOnly(
        nameof(IconSize), typeof(double), typeof(PlayPauseButton), new FrameworkPropertyMetadata(26.0));
    public static readonly DependencyProperty IconSizeProperty = IconSizeKey.DependencyProperty;

    private static readonly DependencyPropertyKey CornerKey = ShapeMorph.RegisterCorner<PlayPauseButton>();
    public static readonly DependencyProperty CornerProperty = CornerKey.DependencyProperty;
    public static readonly DependencyProperty RadiusProperty = ShapeMorph.RegisterRadius<PlayPauseButton>(CornerKey);

    private FrameworkElement? _play, _pause;

    public PlayPauseButton() => OnSizeChangedInternal();

    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double IconSize => (double)GetValue(IconSizeProperty);
    public CornerRadius Corner => (CornerRadius)GetValue(CornerProperty);
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _play = GetTemplateChild("PART_Play") as FrameworkElement;
        _pause = GetTemplateChild("PART_Pause") as FrameworkElement;
        foreach (var el in new[] { _play, _pause })
        {
            if (el is null) continue;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = new ScaleTransform(1, 1);   // template freezables are frozen → own instance
        }
        UpdateVisuals(false);
    }

    protected override void OnChecked(RoutedEventArgs e) { base.OnChecked(e); UpdateVisuals(true); }
    protected override void OnUnchecked(RoutedEventArgs e) { base.OnUnchecked(e); UpdateVisuals(true); }
    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e) { base.OnIsPressedChanged(e); UpdateVisuals(true); }

    private void OnSizeChangedInternal()
    {
        SetValue(IconSizeKey, Math.Round(Size * 0.54));
        UpdateVisuals(false);
    }

    private void UpdateVisuals(bool animate)
    {
        var playing = IsChecked == true;
        var target = IsPressed ? Size * 0.2 : playing ? Size / 2 : Size * 0.3;
        ShapeMorph.To(this, RadiusProperty, target, animate);
        Fade(_play, playing ? 0 : 1, animate);
        Fade(_pause, playing ? 1 : 0, animate);
    }

    private void Fade(FrameworkElement? el, double to, bool animate)
    {
        if (el is null) return;
        if (animate && IsLoaded && !Motion.ReduceMotion) el.Animate(OpacityProperty, to, MotionToken.EffectsDefault);
        else el.Set(OpacityProperty, to);
        if (el.RenderTransform is ScaleTransform st)
        {
            var s = to > 0.5 ? 1.0 : 0.6;
            if (animate && IsLoaded && !Motion.ReduceMotion)
            {
                var token = Motion.Pick(MotionToken.SpatialFast, MotionToken.Pop);
                st.Animate(ScaleTransform.ScaleXProperty, s, token);
                st.Animate(ScaleTransform.ScaleYProperty, s, token);
            }
            else if (!st.IsFrozen)
            {
                st.Set(ScaleTransform.ScaleXProperty, s);
                st.Set(ScaleTransform.ScaleYProperty, s);
            }
        }
    }
}
