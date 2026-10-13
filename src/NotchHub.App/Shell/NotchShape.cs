using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NotchHub.UI.Animation;

namespace NotchHub.App.Shell;

/// <summary>
/// The notch body: a rectangle hanging from the top edge with bottom corner radius Rb and two inverse fillets ("shoulders",
/// Rs) where it meets the top edge. Geometry lives in physics channels (<see cref="W"/>, <see cref="H"/>, <see cref="Rb"/>,
/// <see cref="Rs"/>) and is render-only: the element always measures to the constant stage size, the body is drawn into a
/// retained <see cref="DrawingGroup"/> and the child is clipped by one mutable <see cref="RectangleGeometry"/> — a frame of
/// motion costs no layout and no allocations besides one small StreamGeometry (docs/MOTION.md §3).
/// </summary>
public sealed class NotchShape : Decorator, ISpringSink
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(NotchShape),
        new FrameworkPropertyMetadata(Brushes.Black, (d, _) => ((NotchShape)d).Redraw()));

    public static readonly DependencyProperty GlowBrushProperty = DependencyProperty.Register(nameof(GlowBrush), typeof(Brush), typeof(NotchShape),
        new FrameworkPropertyMetadata(Brushes.White, (d, _) => { var s = (NotchShape)d; s._glowPens = null; s.Redraw(); }));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush GlowBrush { get => (Brush)GetValue(GlowBrushProperty); set => SetValue(GlowBrushProperty, value); }

    // soft glow / shadow = a few concentric strokes with falling alpha (much cheaper than an effect on a layered window)
    private static readonly (double Thickness, double Alpha)[] GlowLayers = [(7, 0.05), (4, 0.09), (2, 0.16), (1, 0.38)];
    private static readonly (double Thickness, double Alpha)[] ShadowLayers = [(26, 0.035), (18, 0.05), (11, 0.07), (5, 0.09)];
    private static readonly Pen[] ShadowPens = ShadowLayers.Select(l => Frozen(new Pen(Brushes.Black, l.Thickness) { LineJoin = PenLineJoin.Round })).ToArray();
    private static readonly Transform ShadowOffset = Frozen(new TranslateTransform(0, 5));

    private readonly DrawingGroup _drawing = new();
    private readonly RectangleGeometry _clip = new();
    private Pen[]? _glowPens;
    private bool _clipAttached;

    public NotchShape()
    {
        W = new SpringChannel(200, this, 0.1);
        H = new SpringChannel(12, this, 0.1);
        Rb = new SpringChannel(9, this, 0.1);
        Rs = new SpringChannel(6, this, 0.1);
        Glow = new SpringChannel(0, this, 1 / 512.0);
        Shadow = new SpringChannel(0, this, 1 / 512.0);
        Redraw();
    }

    public SpringChannel W { get; }
    public SpringChannel H { get; }
    public SpringChannel Rb { get; }
    public SpringChannel Rs { get; }
    /// <summary>0..1 strength of the subtle edge glow (hover / open, adaptive to the background).</summary>
    public SpringChannel Glow { get; }
    /// <summary>0..1 strength of the stroke shadow under the expanded panel.</summary>
    public SpringChannel Shadow { get; }

    /// <summary>Constant layout size (the window); the body is centered horizontally and hangs from the top.</summary>
    public double StageWidth { get; set; } = 1000;
    public double StageHeight { get; set; } = 660;

    /// <summary>Shoulder bloom (Refreshed): fillets flare with the height velocity — "liquid pulled from the top edge".</summary>
    public bool Bloom { get; set; }

    public double BodyWidth => Math.Max(0, W.Value);
    public double BodyHeight => Math.Max(0, H.Value);
    public double ShoulderRadius => Math.Max(0, Rs.Value);

    /// <summary>Fastest size change right now (DIP/s) — drives motion blur.</summary>
    public double Speed => Math.Max(Math.Abs(W.Velocity), Math.Abs(H.Velocity));

    /// <summary>Raised after each redraw caused by a spring frame.</summary>
    public event Action? Frame;

    public void OnSpringFrame()
    {
        Redraw();
        Frame?.Invoke();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(new Size(StageWidth, StageHeight));
        return new Size(StageWidth, StageHeight);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is { } child)
        {
            child.Arrange(new Rect(0, 0, StageWidth, StageHeight));
            if (!_clipAttached) { child.Clip = _clip; _clipAttached = true; }
        }
        return new Size(StageWidth, StageHeight);
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(_drawing);

    private (double X0, double W, double H, double Rs, double Rb) Shape()
    {
        double w = BodyWidth, h = BodyHeight;
        double rs = Math.Max(0, Rs.Value);
        if (Bloom) rs += Math.Clamp(Math.Abs(H.Velocity) / 800, 0, 4);
        rs = Math.Min(rs, h);
        double rb = Math.Clamp(Rb.Value, 0, Math.Min(h, w / 2));
        return ((StageWidth - w) / 2 - rs, w, h, rs, rb);
    }

    private void Redraw()
    {
        var (x0, w, h, rs, rb) = Shape();
        _clip.Rect = new Rect(x0 + rs, -rb, Math.Max(0, w), Math.Max(0, h + rb));
        _clip.RadiusX = _clip.RadiusY = rb;

        if (_drawing.IsFrozen || _redrawing) return;
        var glow = Glow.Value;
        if (glow > 0.01 && _glowPens is null && GlowBrush is { } gb)
        {
            // pens on a frozen copy: freezing the themed brush itself would re-enter through the property callback
            var brush = gb.IsFrozen ? gb : gb.CloneCurrentValue();
            if (brush.CanFreeze) brush.Freeze();
            _glowPens = GlowLayers.Select(l => Frozen(new Pen(brush, l.Thickness))).ToArray();
        }
        _redrawing = true;
        try { Draw(x0, w, h, rs, rb, glow); }
        finally { _redrawing = false; }
    }

    private bool _redrawing;

    private void Draw(double x0, double w, double h, double rs, double rb, double glow)
    {
        using var dc = _drawing.Open();
        if (w <= 0 || h <= 0) return;
        var g = new StreamGeometry();
        using (var c = g.Open()) Trace(c, x0, w, h, rs, rb, closed: true);
        g.Freeze();
        // glow + shadow follow the visible outline only (no stroke along the screen edge), and fade in from the top edge so the
        // shoulders taper off smoothly instead of ending in a hard cut
        var outline = new StreamGeometry();
        using (var c = outline.Open()) Trace(c, x0, w, h, rs, rb, closed: false);
        outline.Freeze();
        var fade = FadeMask(rs);

        var shadow = Shadow.Value;
        if (shadow > 0.01)
        {
            dc.PushOpacityMask(fade);
            dc.PushTransform(ShadowOffset);
            for (int i = 0; i < ShadowPens.Length; i++)
            {
                dc.PushOpacity(ShadowLayers[i].Alpha * shadow);
                dc.DrawGeometry(null, ShadowPens[i], outline);
                dc.Pop();
            }
            dc.Pop();
            dc.Pop();
        }
        dc.DrawGeometry(Fill, null, g);
        if (glow > 0.01 && _glowPens is not null)
        {
            dc.PushOpacityMask(fade);
            for (int i = 0; i < _glowPens.Length; i++)
            {
                dc.PushOpacity(GlowLayers[i].Alpha * glow);
                dc.DrawGeometry(null, _glowPens[i], outline);
                dc.Pop();
            }
            dc.Pop();
        }
    }

    private static void Trace(StreamGeometryContext c, double x0, double w, double h, double rs, double rb, bool closed)
    {
        c.BeginFigure(new Point(x0, 0), closed, closed);
        c.ArcTo(new Point(x0 + rs, rs), new Size(rs, rs), 0, false, SweepDirection.Clockwise, true, true);
        c.LineTo(new Point(x0 + rs, h - rb), true, true);
        c.ArcTo(new Point(x0 + rs + rb, h), new Size(rb, rb), 0, false, SweepDirection.Counterclockwise, true, true);
        c.LineTo(new Point(x0 + rs + w - rb, h), true, true);
        c.ArcTo(new Point(x0 + rs + w, h - rb), new Size(rb, rb), 0, false, SweepDirection.Counterclockwise, true, true);
        c.LineTo(new Point(x0 + rs + w, rs), true, true);
        c.ArcTo(new Point(x0 + w + 2 * rs, 0), new Size(rs, rs), 0, false, SweepDirection.Clockwise, true, true);
    }

    private static readonly Dictionary<int, Brush> FadeMasks = [];

    /// <summary>Vertical alpha ramp (0 at the screen edge → 1 below the shoulders), cached per rounded length.</summary>
    private static Brush FadeMask(double rs)
    {
        var len = (int)Math.Round(Math.Max(10, rs * 1.6 + 6));
        if (FadeMasks.TryGetValue(len, out var b)) return b;
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, len),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                new GradientStop(Color.FromArgb(90, 0, 0, 0), 0.45),
                new GradientStop(Color.FromArgb(255, 0, 0, 0), 1),
            },
        };
        brush.Freeze();
        FadeMasks[len] = brush;
        return brush;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters p)
    {
        var (x0, w, h, rs, _) = Shape();
        var pt = p.HitPoint;
        return pt.X >= x0 && pt.X <= x0 + w + 2 * rs && pt.Y >= 0 && pt.Y <= h ? new PointHitTestResult(this, pt) : null;
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        if (f.CanFreeze) f.Freeze();
        return f;
    }
}

/// <summary>
/// Measures its child without a height limit (and optionally without a width limit), reports the natural size via
/// <see cref="NaturalSizeChanged"/> and arranges the child within <see cref="MaxChildHeight"/>-bounded space.
/// Lets the shell size the notch to the desired size of black-box views.
/// </summary>
public sealed class MeasureHost : Decorator
{
    public Size NaturalSize { get; private set; }
    public double FixedWidth { get; set; } = double.NaN;
    public double MaxChildHeight { get; set; } = double.PositiveInfinity;
    public event EventHandler? NaturalSizeChanged;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        var w = double.IsNaN(FixedWidth) ? double.PositiveInfinity : FixedWidth;
        Child.Measure(new Size(w, double.PositiveInfinity));
        var natural = Child.DesiredSize;
        if (natural.Height > MaxChildHeight) Child.Measure(new Size(w, MaxChildHeight));
        if (!double.IsNaN(FixedWidth)) natural.Width = FixedWidth;
        if (Math.Abs(natural.Width - NaturalSize.Width) > 0.5 || Math.Abs(natural.Height - NaturalSize.Height) > 0.5)
        {
            NaturalSize = natural;
            Dispatcher.BeginInvoke(() => NaturalSizeChanged?.Invoke(this, EventArgs.Empty));
        }
        return new Size(natural.Width, Math.Min(natural.Height, MaxChildHeight));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}

/// <summary>Attached "press squish": scales the element down while pressed (Press, no bounce) and springs back (Release).</summary>
public static class Tactile
{
    public static readonly DependencyProperty SquishProperty = DependencyProperty.RegisterAttached("Squish", typeof(double), typeof(Tactile),
        new PropertyMetadata(0.0, OnChanged));

    public static void SetSquish(UIElement e, double v) => e.SetValue(SquishProperty, v);
    public static double GetSquish(UIElement e) => (double)e.GetValue(SquishProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || (double)e.OldValue != 0) return;
        var scale = new ScaleTransform(1, 1);
        el.RenderTransform = scale;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        void Press(double to)
        {
            var token = to < 1 ? MotionToken.Press : MotionToken.Release;
            scale.Animate(ScaleTransform.ScaleXProperty, to, token);
            scale.Animate(ScaleTransform.ScaleYProperty, to, token);
        }
        el.PreviewMouseLeftButtonDown += (_, _) => Press(1 - GetSquish(el));
        el.PreviewMouseLeftButtonUp += (_, _) => Press(1);
        el.MouseLeave += (_, _) => { if (scale.ScaleX != 1) Press(1); };
        el.LostMouseCapture += (_, _) => { if (scale.ScaleX != 1) Press(1); };
        el.MouseEnter += (_, _) => { if (Mouse.LeftButton != MouseButtonState.Pressed && scale.ScaleX != 1) Press(1); };
    }
}

/// <summary>
/// Active-tab pill drawn from two physics channels (left edge X and width) — render-only, no Width animation / layout.
/// Width never overshoots (Data spring) so the pill never pokes past its label; the position may spring.
/// </summary>
public sealed class TabPill : FrameworkElement, ISpringSink
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(TabPill),
        new FrameworkPropertyMetadata(Brushes.Gray, (d, _) => ((TabPill)d).Redraw()));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    private readonly DrawingGroup _drawing = new();

    public TabPill()
    {
        X = new SpringChannel(0, this, 0.05);
        PillWidth = new SpringChannel(0, this, 0.05);
        Alpha = new SpringChannel(0, this, 1 / 512.0);
        IsHitTestVisible = false;
        SizeChanged += (_, _) => Redraw();
    }

    public SpringChannel X { get; }
    public SpringChannel PillWidth { get; }
    public SpringChannel Alpha { get; }

    public void OnSpringFrame() => Redraw();

    protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(_drawing);

    private void Redraw()
    {
        using var dc = _drawing.Open();
        double w = PillWidth.Value, h = ActualHeight, a = Alpha.Value;
        if (w <= 0.5 || h <= 0 || a <= 0.002) return;
        var r = Math.Min(h, w) / 2;
        if (a < 0.998) dc.PushOpacity(a);
        dc.DrawRoundedRectangle(Fill, null, new Rect(X.Value, 0, w, h), r, r);
        if (a < 0.998) dc.Pop();
    }
}

/// <summary>
/// Physics state of one content layer: opacity, scale (top-center origin), translate X/Y and an entrance blur, plus
/// Smooth-profile motion blur from its own and the container's velocity. Collapses the element when it fades out and is
/// no longer wanted. One SetValue batch per frame; the BlurEffect is removed at rest.
/// </summary>
internal sealed class LayerMotion : ISpringSink
{
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _move = new();
    private readonly Func<bool> _isWanted;
    private readonly NotchShape? _container;
    private BlurEffect? _blur;

    public LayerMotion(FrameworkElement element, Func<bool> isWanted, NotchShape? container, bool entranceBlur, double maxMotionBlur)
    {
        Element = element;
        _isWanted = isWanted;
        _container = container;
        EntranceBlur = entranceBlur;
        MaxMotionBlur = maxMotionBlur;
        element.RenderTransformOrigin = new Point(0.5, 0);
        element.RenderTransform = new TransformGroup { Children = { _scale, _move } };
        Alpha = new SpringChannel(element.Opacity, this, 1 / 512.0);
        Scale = new SpringChannel(1, this, 1 / 2000.0);
        X = new SpringChannel(0, this, 0.05);
        Y = new SpringChannel(0, this, 0.05);
        Blur = new SpringChannel(0, this, 0.05);
        Alpha.Settled += OnAlphaSettled;
    }

    public FrameworkElement Element { get; }
    public SpringChannel Alpha { get; }
    public SpringChannel Scale { get; }
    public SpringChannel X { get; }
    public SpringChannel Y { get; }
    public SpringChannel Blur { get; }
    /// <summary>Entrance / exit blur allowed (small layers only — never the 820×640 panel).</summary>
    public bool EntranceBlur { get; }
    public double MaxMotionBlur { get; }

    /// <summary>Hidden or fully transparent: entrance start values may be applied.</summary>
    public bool IsOut => Element.Visibility != Visibility.Visible || (Alpha.Value <= 0.002 && Alpha.FinalTarget <= 0.002);

    public void Show() => Element.Visibility = Visibility.Visible;

    /// <summary>Jumps every channel (no motion).</summary>
    public void Snap(double alpha, double scale = 1, double x = 0, double y = 0, double blur = 0)
    {
        Alpha.Snap(alpha, false);
        Scale.Snap(scale, false);
        X.Snap(x, false);
        Y.Snap(y, false);
        Blur.Snap(EntranceBlur ? blur : 0, false);
        OnSpringFrame();
        if (alpha <= 0 && !_isWanted()) Element.Visibility = Visibility.Collapsed;
    }

    public void OnSpringFrame()
    {
        Element.Opacity = Alpha.Value;
        _scale.ScaleX = _scale.ScaleY = Scale.Value;
        _move.X = X.Value;
        _move.Y = Y.Value;
        UpdateBlur();
    }

    /// <summary>Re-evaluates the blur (also called on container frames for motion blur).</summary>
    public void UpdateBlur()
    {
        double r = EntranceBlur ? Math.Max(0, Blur.Value) : 0;
        if (Motion.IsSmooth && Element.Visibility == Visibility.Visible && Alpha.Value > 0.01)
        {
            var speed = (_container?.Speed ?? 0) * 0.0015 + (Math.Abs(X.Velocity) + Math.Abs(Y.Velocity)) * 0.02 + Math.Abs(Scale.Velocity) * 6;
            r = Math.Max(r, Motion.MotionBlur(speed, 1, MaxMotionBlur));
        }
        if (r < 0.25 || !Motion.CanBlur)
        {
            if (Element.Effect is not null) Element.Effect = null;
            return;
        }
        _blur ??= new BlurEffect { RenderingBias = RenderingBias.Performance, KernelType = KernelType.Gaussian };
        _blur.Radius = r;
        if (!ReferenceEquals(Element.Effect, _blur)) Element.Effect = _blur;
    }

    /// <summary>Collapses the element now if it is already transparent at rest and no longer wanted.</summary>
    public void CheckHidden()
    {
        if (!Alpha.IsAnimating) OnAlphaSettled(Alpha);
    }

    private void OnAlphaSettled(SpringChannel c)
    {
        if (c.Value > 0.002 || _isWanted()) return;
        Element.Visibility = Visibility.Collapsed;
        Element.Effect = null;
    }
}
