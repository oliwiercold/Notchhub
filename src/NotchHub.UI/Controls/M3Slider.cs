using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NotchHub.UI.Animation;

namespace NotchHub.UI.Controls;

internal static class Geo
{
    /// <summary>Rectangle with independent left / right corner radii (clamped to fit).</summary>
    public static StreamGeometry RoundRect(Rect r, double left, double right)
    {
        if (left + right > r.Width && left + right > 0)
        {
            var k = r.Width / (left + right);
            left *= k; right *= k;
        }
        left = Math.Min(left, r.Height / 2);
        right = Math.Min(right, r.Height / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + left, r.Top), true, true);
            c.LineTo(new Point(r.Right - right, r.Top), false, false);
            c.ArcTo(new Point(r.Right, r.Top + right), new Size(right, right), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Right, r.Bottom - right), false, false);
            c.ArcTo(new Point(r.Right - right, r.Bottom), new Size(right, right), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Left + left, r.Bottom), false, false);
            c.ArcTo(new Point(r.Left, r.Bottom - left), new Size(left, left), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(r.Left, r.Top + left), false, false);
            c.ArcTo(new Point(r.Left + left, r.Top), new Size(left, left), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    private static readonly Typeface LabelFace = new(NhFonts.Sans, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface SymbolFace = new(NhFonts.Symbols, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SymbolFilledFace = new(NhFonts.SymbolsFilled, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static FormattedText Label(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, size, brush, pixelsPerDip);

    /// <summary>Draws a Material Symbols glyph with its em square at (x, centerY - size/2).</summary>
    public static void DrawIcon(DrawingContext dc, string name, double size, Brush brush, double x, double centerY, double pixelsPerDip, bool filled = true)
    {
        var ft = new FormattedText(IconMap.Get(name), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            filled ? SymbolFilledFace : SymbolFace, size, brush, pixelsPerDip);
        dc.DrawText(ft, new Point(x, centerY + size / 2 - ft.Baseline));
    }
}

/// <summary>
/// M3 Expressive slider: thick active track (left) + inactive track (right) separated by gaps around a bar handle
/// that narrows while dragging; icon inside the active track, value label at the right end (or a stop dot).
/// Brushes: ActiveBrush (Primary), TrackBrush (Surface3), OnActiveBrush (OnPrimary), Foreground (labels on the track).
/// </summary>
public class M3Slider : RangeBase
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(M3Slider), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowValueProperty = DependencyProperty.Register(
        nameof(ShowValue), typeof(bool), typeof(M3Slider), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(M3Slider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackHeightProperty = DependencyProperty.Register(
        nameof(TrackHeight), typeof(double), typeof(M3Slider), new FrameworkPropertyMetadata(44.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveBrushProperty = DependencyProperty.Register(
        nameof(ActiveBrush), typeof(Brush), typeof(M3Slider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(M3Slider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OnActiveBrushProperty = DependencyProperty.Register(
        nameof(OnActiveBrush), typeof(Brush), typeof(M3Slider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        "DisplayFraction", typeof(double), typeof(M3Slider), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty HandleWidthProperty = DependencyProperty.Register(
        "HandleWidth", typeof(double), typeof(M3Slider), new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly RoutedEvent DragStartedEvent = EventManager.RegisterRoutedEvent(
        nameof(DragStarted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(M3Slider));
    public static readonly RoutedEvent DragCompletedEvent = EventManager.RegisterRoutedEvent(
        nameof(DragCompleted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(M3Slider));

    private const double Gap = 6;

    static M3Slider()
    {
        FocusableProperty.OverrideMetadata(typeof(M3Slider), new FrameworkPropertyMetadata(true));
        MaximumProperty.OverrideMetadata(typeof(M3Slider), new FrameworkPropertyMetadata(1.0));
        SmallChangeProperty.OverrideMetadata(typeof(M3Slider), new FrameworkPropertyMetadata(0.05));
        LargeChangeProperty.OverrideMetadata(typeof(M3Slider), new FrameworkPropertyMetadata(0.2));
    }

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool ShowValue { get => (bool)GetValue(ShowValueProperty); set => SetValue(ShowValueProperty, value); }
    /// <summary>Label text; null → percentage of the range ("64%").</summary>
    public string? ValueText { get => (string?)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public double TrackHeight { get => (double)GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush? OnActiveBrush { get => (Brush?)GetValue(OnActiveBrushProperty); set => SetValue(OnActiveBrushProperty, value); }

    /// <summary>True while the user drags with the mouse.</summary>
    public bool IsDragging { get; private set; }

    public event RoutedEventHandler DragStarted { add => AddHandler(DragStartedEvent, value); remove => RemoveHandler(DragStartedEvent, value); }
    public event RoutedEventHandler DragCompleted { add => AddHandler(DragCompletedEvent, value); remove => RemoveHandler(DragCompletedEvent, value); }

    private double Fraction => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        SyncDisplay(!IsDragging);
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum) { base.OnMinimumChanged(oldMinimum, newMinimum); SyncDisplay(false); }
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum) { base.OnMaximumChanged(oldMaximum, newMaximum); SyncDisplay(false); }

    private bool _clickJump;

    private void SyncDisplay(bool animate)
    {
        if (IsDragging && Motion.IsRefreshed && !Motion.ReduceMotion && this.IsAnimating(DisplayProperty))
        {
            this.Animate(DisplayProperty, Fraction, new Spring(0.12, 1.0));   // finish the click jump, then 1:1
            return;
        }
        if (!animate || !IsLoaded || Motion.ReduceMotion) { this.Set(DisplayProperty, Fraction); return; }
        if (!Motion.IsRefreshed) this.Animate(DisplayProperty, Fraction, MotionToken.SpatialFast);
        // Refreshed: a click on the track springs quickly to the pointer; keyboard / wheel / external changes use Data
        // (no overshoot, velocity kept across key-repeat bursts)
        else if (_clickJump) this.Animate(DisplayProperty, Fraction, new Spring(0.12, 1.0));
        else this.Animate(DisplayProperty, Fraction, MotionToken.Data);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size constraint) => new(64, TrackHeight + 12);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var th = Math.Min(TrackHeight, h);
        var top = (h - th) / 2;
        var mid = h / 2;
        var frac = Math.Clamp((double)GetValue(DisplayProperty), 0, 1);
        var hw = (double)GetValue(HandleWidthProperty);
        var hx = frac * (w - hw);
        double outer = Math.Min(14, th / 2), inner = Math.Min(4, th / 2);
        var active = ActiveBrush ?? Brushes.White;
        var onActive = OnActiveBrush ?? Brushes.Black;
        var fg = Foreground ?? Brushes.Gray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // hit-testable everywhere
        var activeRight = hx - Gap;
        var inactiveLeft = hx + hw + Gap;
        if (activeRight > 0.5) dc.DrawGeometry(active, null, Geo.RoundRect(new Rect(0, top, activeRight, th), outer, inner));
        if (inactiveLeft < w - 0.5) dc.DrawGeometry(TrackBrush, null, Geo.RoundRect(new Rect(inactiveLeft, top, w - inactiveLeft, th), inner, outer));
        dc.DrawRoundedRectangle(active, null, new Rect(hx, Math.Max(0, top - 6), hw, Math.Min(h, th + 12)), hw / 2, hw / 2);

        // value label (or stop dot) at the right end
        var labelLeft = w;
        if (ShowValue)
        {
            var text = ValueText ?? Math.Round(Fraction * 100).ToString(CultureInfo.CurrentCulture) + "%";
            var ft = Geo.Label(text, 12, fg, dpi);
            var x = w - 14 - ft.Width;
            if (x > inactiveLeft + 6)
            {
                dc.DrawText(ft, new Point(x, mid - ft.Height / 2));
                labelLeft = x;
            }
            else if (activeRight - ft.Width - 14 > 44)
            {
                ft.SetForegroundBrush(onActive);
                dc.DrawText(ft, new Point(activeRight - 12 - ft.Width, mid - ft.Height / 2));
            }
        }
        else if (w - inactiveLeft > 18)
        {
            dc.DrawEllipse(active, null, new Point(w - Math.Max(10, outer - 2), mid), 2, 2);
            labelLeft = w - 18;
        }

        // icon: inside the active track, or right of the handle when the active part is too short
        var icon = Icon;
        if (!string.IsNullOrEmpty(icon))
        {
            var size = Math.Min(22, th - 14);
            if (size >= 10)
            {
                if (activeRight >= size + 24) Geo.DrawIcon(dc, icon, size, onActive, 12, mid, dpi);
                else if (inactiveLeft + size + 20 < labelLeft) Geo.DrawIcon(dc, icon, size, fg, inactiveLeft + 10, mid, dpi);
            }
        }
    }

    // ───────────── input ─────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        Focus();
        if (!CaptureMouse()) return;
        _clickJump = true;
        try { SetFromX(e.GetPosition(this).X); }   // animated jump to the click position
        finally { _clickJump = false; }
        IsDragging = true;
        AnimateHandle(2);
        RaiseEvent(new RoutedEventArgs(DragStartedEvent, this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsDragging) SetFromX(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsDragging) { ReleaseMouseCapture(); e.Handled = true; }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!IsDragging) return;
        IsDragging = false;
        AnimateHandle(4);
        RaiseEvent(new RoutedEventArgs(DragCompletedEvent, this));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEnabled || e.Delta == 0) return;
        Value += Math.Sign(e.Delta) * SmallChange;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left or Key.Down: Value -= SmallChange; break;
            case Key.Right or Key.Up: Value += SmallChange; break;
            case Key.PageDown: Value -= LargeChange; break;
            case Key.PageUp: Value += LargeChange; break;
            case Key.Home: Value = Minimum; break;
            case Key.End: Value = Maximum; break;
            default: return;
        }
        e.Handled = true;
    }

    private void SetFromX(double x)
    {
        var hw = (double)GetValue(HandleWidthProperty);
        var usable = Math.Max(1, ActualWidth - hw);
        var f = Math.Clamp((x - hw / 2) / usable, 0, 1);
        Value = Minimum + f * (Maximum - Minimum);
    }

    private void AnimateHandle(double to)
    {
        if (Motion.ReduceMotion) this.Set(HandleWidthProperty, to);
        // M3: the handle halves while pressed (Fx, no bounce) and springs back on release
        else this.Animate(HandleWidthProperty, to, Motion.Pick(MotionToken.SpatialFast, to < 3 ? MotionToken.Fx : MotionToken.Release));
    }
}
