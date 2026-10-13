using System.Windows;
using System.Windows.Media;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Stats;

/// <summary>Thin rounded linear bar (track + fill) whose fill animates with a spring when <see cref="Value"/> (0..1) changes.</summary>
public sealed class RatioBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RatioBar),
        new PropertyMetadata(0.0, (d, e) => ((RatioBar)d).OnValueChanged((double)e.NewValue)));

    private static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        "Display", typeof(double), typeof(RatioBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(RatioBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(RatioBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public RatioBar()
    {
        Height = 4;
        SnapsToDevicePixels = true;
    }

    private void OnValueChanged(double v)
    {
        v = double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
        if (!IsLoaded || Motion.ReduceMotion) { BeginAnimation(DisplayProperty, null); SetValue(DisplayProperty, v); return; }
        this.Animate(DisplayProperty, v, Springs.SpatialDefault);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double r = h / 2;
        if (Track is { } track) dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), r, r);
        double f = Math.Clamp((double)GetValue(DisplayProperty), 0, 1);
        if (f > 0.002 && Fill is { } fill)
            dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, Math.Max(h, w * f), h), r, r);
    }
}
