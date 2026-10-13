using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotchHub.UI.Animation;

namespace NotchHub.UI.Controls;

/// <summary>
/// M3 Expressive loading indicator: a rotating shape that morphs between soft rounded polygons.
/// Frames are precomputed once (frozen unit geometries); while active and visible it only picks a frame per tick.
/// </summary>
public class LoadingIndicator : Control
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LoadingIndicator), new FrameworkPropertyMetadata(40.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(LoadingIndicator), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ContainerBrushProperty = DependencyProperty.Register(
        nameof(ContainerBrush), typeof(Brush), typeof(LoadingIndicator), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(LoadingIndicator), new FrameworkPropertyMetadata(true, (d, _) => ((LoadingIndicator)d).UpdateRunning()));
    public static readonly DependencyProperty ContainedProperty = DependencyProperty.Register(
        nameof(Contained), typeof(bool), typeof(LoadingIndicator), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    // (lobes, depth) of each soft polygon: cookie, clover, pentagon, oval, burst, triangle, sunny
    private static readonly (int N, double K)[] Shapes = [(7, 0.10), (4, 0.16), (5, 0.12), (2, 0.10), (8, 0.11), (3, 0.13), (12, 0.06)];
    private const int FramesPerShape = 40, Fps = 60;
    private static Geometry[]? _frames;

    private readonly Stopwatch _clock = new();
    private bool _running;
    private int _frame;
    private MatrixTransform? _xf;
    private Size _xfSize;

    public LoadingIndicator()
    {
        IsVisibleChanged += (_, _) => UpdateRunning();
        Loaded += (_, _) => UpdateRunning();
        Unloaded += (_, _) => UpdateRunning();
    }

    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Brush? Brush { get => (Brush?)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    /// <summary>Background circle when <see cref="Contained"/> (default PrimaryContainer).</summary>
    public Brush? ContainerBrush { get => (Brush?)GetValue(ContainerBrushProperty); set => SetValue(ContainerBrushProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public bool Contained { get => (bool)GetValue(ContainedProperty); set => SetValue(ContainedProperty, value); }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint) => new(Size, Size);

    private void UpdateRunning()
    {
        var run = IsActive && IsVisible && IsLoaded && !Motion.ReduceMotion;
        if (run == _running) return;
        _running = run;
        if (run)
        {
            _clock.Start();
            CompositionTarget.Rendering += OnTick;
        }
        else
        {
            _clock.Stop();
            CompositionTarget.Rendering -= OnTick;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var total = Shapes.Length * FramesPerShape;
        var f = (int)(_clock.Elapsed.TotalSeconds * Fps) % total;
        if (f == _frame) return;
        _frame = f;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var size = Math.Min(Size, Math.Min(w, h));
        if (size <= 0 || !IsActive) return;
        var c = new Point(w / 2, h / 2);
        var r = size / 2;
        if (Contained)
        {
            dc.DrawEllipse(ContainerBrush, null, c, r, r);
            r *= 0.62;
        }
        else r *= 0.9;
        var key = new System.Windows.Size(w, h * 1000 + r);
        if (_xf is null || _xfSize != key)
        {
            _xf = new MatrixTransform(new Matrix(r, 0, 0, r, c.X, c.Y));
            _xf.Freeze();
            _xfSize = key;
        }
        var frames = _frames ??= BuildFrames();
        dc.PushTransform(_xf);
        dc.DrawGeometry(Brush ?? Brushes.White, null, frames[_frame % frames.Length]);
        dc.Pop();
    }

    private static Geometry[] BuildFrames()
    {
        var total = Shapes.Length * FramesPerShape;
        var frames = new Geometry[total];
        const int points = 96;
        for (var f = 0; f < total; f++)
        {
            var shape = f / FramesPerShape;
            var local = (f % FramesPerShape) / (double)FramesPerShape;
            // hold for 40 %, then morph with an ease-in-out to the next shape
            var t = local < 0.4 ? 0 : (local - 0.4) / 0.6;
            t = t * t * (3 - 2 * t);
            var (n1, k1) = Shapes[shape];
            var (n2, k2) = Shapes[(shape + 1) % Shapes.Length];
            // constant spin + an extra quarter turn kick during each morph
            var rotation = f / (double)total * 2 * Math.PI * 3 + (shape + t) * Math.PI / 2;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var pts = new List<Point>(points);
                Point At(double a)
                {
                    var r1 = (1 + k1 * Math.Cos(n1 * a)) / (1 + k1);
                    var r2 = (1 + k2 * Math.Cos(n2 * a)) / (1 + k2);
                    var rr = r1 + (r2 - r1) * t;
                    return new Point(rr * Math.Cos(a + rotation), rr * Math.Sin(a + rotation));
                }
                ctx.BeginFigure(At(0), true, true);
                for (var i = 1; i < points; i++) pts.Add(At(i * 2 * Math.PI / points));
                ctx.PolyLineTo(pts, false, true);
            }
            g.Freeze();
            frames[f] = g;
        }
        return frames;
    }
}
