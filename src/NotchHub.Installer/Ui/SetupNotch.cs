using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NotchHub.Installer.Ui;

/// <summary>
/// The setup notch: a body hanging from the top edge with rounded bottom corners and two inverse fillets ("shoulders") where
/// it meets the screen edge — the same silhouette as the app's notch. Body size / radius are render-only DPs animated with the
/// app's springs; the child keeps its own (target) size and is revealed through a clip that follows the body.
/// </summary>
public sealed class SetupNotch : Decorator
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SetupNotch),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutlineProperty = DependencyProperty.Register(nameof(Outline), typeof(Brush), typeof(SetupNotch),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BodyWidthProperty = DependencyProperty.Register(nameof(BodyWidth), typeof(double), typeof(SetupNotch),
        new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public static readonly DependencyProperty BodyHeightProperty = DependencyProperty.Register(nameof(BodyHeight), typeof(double), typeof(SetupNotch),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(SetupNotch),
        new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public static readonly DependencyProperty ShoulderProperty = DependencyProperty.Register(nameof(Shoulder), typeof(double), typeof(SetupNotch),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));
    public static readonly DependencyProperty OutlineOpacityProperty = DependencyProperty.Register(nameof(OutlineOpacity), typeof(double), typeof(SetupNotch),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Outline { get => (Brush?)GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }
    public double BodyWidth { get => (double)GetValue(BodyWidthProperty); set => SetValue(BodyWidthProperty, value); }
    public double BodyHeight { get => (double)GetValue(BodyHeightProperty); set => SetValue(BodyHeightProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double Shoulder { get => (double)GetValue(ShoulderProperty); set => SetValue(ShoulderProperty, value); }
    public double OutlineOpacity { get => (double)GetValue(OutlineOpacityProperty); set => SetValue(OutlineOpacityProperty, value); }

    private readonly RectangleGeometry _clip = new();
    private readonly TranslateTransform _clipOffset = new();

    public SetupNotch()
    {
        _clip.Transform = _clipOffset;
    }

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SetupNotch)d).UpdateClip();

    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(0, 0); // stage-sized by the parent (stretch); content never drives the window size
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is { } child)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(Math.Round((arrangeSize.Width - size.Width) / 2), 0, size.Width, size.Height));
        }
        UpdateClip(arrangeSize);
        return arrangeSize;
    }

    private void UpdateClip() => UpdateClip(RenderSize);

    private void UpdateClip(Size stage)
    {
        if (Child is not { } child) return;
        var w = Math.Max(0, BodyWidth);
        var h = Math.Max(0, BodyHeight);
        var r = Math.Min(Radius, Math.Min(w, h) / 2);
        var x = (stage.Width - w) / 2;
        // the clip extends above the top edge so only the bottom corners are rounded
        _clip.Rect = new Rect(x, -r, w, h + r);
        _clip.RadiusX = _clip.RadiusY = r;
        var childX = Math.Round((stage.Width - child.DesiredSize.Width) / 2);
        _clipOffset.X = -childX;
        child.Clip = _clip;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Max(0, BodyWidth);
        var h = Math.Max(0, BodyHeight);
        if (w < 1 || h < 1) return;
        var r = Math.Min(Radius, Math.Min(w, h) / 2);
        var s = Math.Min(Shoulder, h);
        var x0 = (RenderSize.Width - w) / 2;
        var x1 = x0 + w;

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0 - s, 0), true, true);
            c.LineTo(new Point(x1 + s, 0), true, false);
            if (s > 0) c.ArcTo(new Point(x1, s), new Size(s, s), 0, false, SweepDirection.Counterclockwise, true, false);
            c.LineTo(new Point(x1, h - r), true, false);
            if (r > 0) c.ArcTo(new Point(x1 - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x0 + r, h), true, false);
            if (r > 0) c.ArcTo(new Point(x0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(x0, s), true, false);
            if (s > 0) c.ArcTo(new Point(x0 - s, 0), new Size(s, s), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(Fill, null, g);

        if (Outline is { } outline && OutlineOpacity > 0.01)
        {
            // subtle accent rim along the sides and the bottom (like the app's glow when the panel is open)
            dc.PushOpacity(OutlineOpacity);
            dc.DrawGeometry(null, new Pen(outline, 1), g);
            dc.Pop();
        }
    }
}
