using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NotchHub.UI.Animation;

namespace NotchHub.UI.Controls;

/// <summary>
/// M3 Expressive wavy linear progress: played part is a sine wave (flattens when <see cref="IsWavy"/> is false),
/// a gap, then a flat track with a stop dot. The wave flows only while <see cref="IsAnimating"/> &amp;&amp; visible
/// (a composition-side TranslateTransform animation — no per-frame managed work). Optional click/drag seeking.
/// </summary>
public class WavyProgressBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(WavyProgressBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsWavyProperty = DependencyProperty.Register(
        nameof(IsWavy), typeof(bool), typeof(WavyProgressBar), new FrameworkPropertyMetadata(true, (d, _) => ((WavyProgressBar)d).OnWaveChanged()));
    public static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(
        nameof(IsAnimating), typeof(bool), typeof(WavyProgressBar), new FrameworkPropertyMetadata(false, (d, _) => ((WavyProgressBar)d).UpdatePhase()));
    public static readonly DependencyProperty AmplitudeProperty = DependencyProperty.Register(
        nameof(Amplitude), typeof(double), typeof(WavyProgressBar), new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((WavyProgressBar)d).OnWaveChanged()));
    public static readonly DependencyProperty WavelengthProperty = DependencyProperty.Register(
        nameof(Wavelength), typeof(double), typeof(WavyProgressBar), new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((WavyProgressBar)d).RestartPhase()));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(WavyProgressBar), new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveBrushProperty = DependencyProperty.Register(
        nameof(ActiveBrush), typeof(Brush), typeof(WavyProgressBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(WavyProgressBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsSeekableProperty = DependencyProperty.Register(
        nameof(IsSeekable), typeof(bool), typeof(WavyProgressBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyPropertyKey IsSeekingKey = DependencyProperty.RegisterReadOnly(
        nameof(IsSeeking), typeof(bool), typeof(WavyProgressBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsSeekingProperty = IsSeekingKey.DependencyProperty;

    private static readonly DependencyProperty CurrentAmplitudeProperty = DependencyProperty.Register(
        "CurrentAmplitude", typeof(double), typeof(WavyProgressBar), new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly TranslateTransform _phase = new();
    private StreamGeometry? _wave;
    private (double W, double H, double A, double L) _waveKey;
    private bool _phaseRunning;
    private double _seek;

    public WavyProgressBar()
    {
        IsVisibleChanged += (_, _) => UpdatePhase();
        Loaded += (_, _) => UpdatePhase();
        Unloaded += (_, _) => UpdatePhase();
    }

    /// <summary>Progress 0..1.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Wave shape (true while playing); false animates the amplitude to 0 (paused).</summary>
    public bool IsWavy { get => (bool)GetValue(IsWavyProperty); set => SetValue(IsWavyProperty, value); }
    /// <summary>Lets the wave flow (phase animation). Runs only while visible.</summary>
    public bool IsAnimating { get => (bool)GetValue(IsAnimatingProperty); set => SetValue(IsAnimatingProperty, value); }
    public double Amplitude { get => (double)GetValue(AmplitudeProperty); set => SetValue(AmplitudeProperty, value); }
    public double Wavelength { get => (double)GetValue(WavelengthProperty); set => SetValue(WavelengthProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public bool IsSeekable { get => (bool)GetValue(IsSeekableProperty); set => SetValue(IsSeekableProperty, value); }
    /// <summary>True while the user drags (the bar shows the drag position instead of <see cref="Value"/>).</summary>
    public bool IsSeeking => (bool)GetValue(IsSeekingProperty);

    /// <summary>Raised when a click / drag seek ends, with the chosen position 0..1.</summary>
    public event EventHandler<double>? SeekCompleted;

    private void OnWaveChanged()
    {
        var target = IsWavy ? Amplitude : 0;
        if (IsLoaded && !Motion.ReduceMotion) this.Animate(CurrentAmplitudeProperty, target, MotionToken.EffectsSlow);
        else this.Set(CurrentAmplitudeProperty, target);
        UpdatePhase();
    }

    private void RestartPhase()
    {
        _wave = null;
        if (_phaseRunning) { _phaseRunning = false; UpdatePhase(); }
    }

    private void UpdatePhase()
    {
        var run = IsAnimating && IsWavy && IsVisible && IsLoaded && !Motion.ReduceMotion && Amplitude > 0;
        if (run == _phaseRunning) return;
        _phaseRunning = run;
        if (run)
        {
            var l = Math.Max(4, Wavelength);
            var anim = new DoubleAnimation(-l, 0, TimeSpan.FromSeconds(l / 24.0)) { RepeatBehavior = RepeatBehavior.Forever };
            // decorative flow: 30 fps is plenty and keeps the layered window cheap
            Timeline.SetDesiredFrameRate(anim, 30);
            _phase.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            _phase.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = Thickness + 2 * Amplitude + 4;
        if (IsSeekable) h = Math.Max(h, Thickness + 14);
        return new Size(32, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var th = Thickness;
        var mid = h / 2;
        var value = Math.Clamp(IsSeeking ? _seek : Value, 0, 1);
        var px = value * w;
        var amp = (double)GetValue(CurrentAmplitudeProperty);
        var active = ActiveBrush ?? Brushes.White;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        if (px > th)
        {
            var pen = new Pen(active, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (Math.Abs(amp) < 0.15)
            {
                dc.DrawLine(pen, new Point(th / 2, mid), new Point(px - th / 2, mid));
            }
            else
            {
                var wave = GetWave(w, h, amp);
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, px, h)));
                dc.PushTransform(_phase);
                dc.DrawGeometry(null, pen, wave);
                dc.Pop();
                dc.Pop();
            }
        }

        var gap = 4 + th / 2;
        var trackStart = Math.Max(px + gap, th / 2);
        if (px <= th) trackStart = th / 2;
        if (trackStart < w - th / 2)
        {
            var trackPen = new Pen(TrackBrush ?? Brushes.Gray, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(trackPen, new Point(trackStart, mid), new Point(w - th / 2, mid));
            if (w - th / 2 - trackStart > th * 2) dc.DrawEllipse(active, null, new Point(w - th / 2, mid), th / 2, th / 2);   // stop dot
        }

        if (IsSeekable && (IsSeeking || IsMouseOver))
        {
            var hh = Math.Min(h, th + 12);
            dc.DrawRoundedRectangle(active, null, new Rect(Math.Clamp(px - 2, 0, w - 4), mid - hh / 2, 4, hh), 2, 2);
        }
    }

    private StreamGeometry GetWave(double w, double h, double amp)
    {
        var l = Math.Max(4, Wavelength);
        var key = (w, h, Math.Round(amp, 2), l);
        if (_wave is not null && _waveKey == key) return _wave;
        var mid = h / 2;
        var step = l / 16;
        var pts = new List<Point>((int)((w + 2 * l) / step) + 2);
        var x0 = -l;
        for (var x = x0 + step; x <= w + l; x += step)
            pts.Add(new Point(x, mid + amp * Math.Sin(2 * Math.PI * x / l)));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0, mid + amp * Math.Sin(2 * Math.PI * x0 / l)), false, false);
            c.PolyLineTo(pts, true, true);
        }
        g.Freeze();
        _wave = g;
        _waveKey = key;
        return g;
    }

    // ───────────── seeking ─────────────

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); if (IsSeekable) InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); if (IsSeekable) InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsSeekable || !IsEnabled || !CaptureMouse()) return;
        _seek = Math.Clamp(e.GetPosition(this).X / Math.Max(1, ActualWidth), 0, 1);
        SetValue(IsSeekingKey, true);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsSeeking) return;
        _seek = Math.Clamp(e.GetPosition(this).X / Math.Max(1, ActualWidth), 0, 1);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsSeeking) { ReleaseMouseCapture(); e.Handled = true; }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!IsSeeking) return;
        SetValue(IsSeekingKey, false);
        SeekCompleted?.Invoke(this, _seek);
    }
}

/// <summary>
/// Circular wavy gauge from 12 o'clock clockwise: wavy active arc, gaps, flat track arc. Value changes spring-animate.
/// Content (e.g. "37%") is centered.
/// </summary>
public class WavyRing : ContentControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(WavyRing), new FrameworkPropertyMetadata(0.0, (d, _) => ((WavyRing)d).SyncDisplay()));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(WavyRing), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveBrushProperty = DependencyProperty.Register(
        nameof(ActiveBrush), typeof(Brush), typeof(WavyRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(WavyRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsWavyProperty = DependencyProperty.Register(
        nameof(IsWavy), typeof(bool), typeof(WavyRing), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WaveCountProperty = DependencyProperty.Register(
        nameof(WaveCount), typeof(int), typeof(WavyRing), new FrameworkPropertyMetadata(10, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AmplitudeProperty = DependencyProperty.Register(
        nameof(Amplitude), typeof(double), typeof(WavyRing), new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GapDegreesProperty = DependencyProperty.Register(
        nameof(GapDegrees), typeof(double), typeof(WavyRing), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        "DisplayValue", typeof(double), typeof(WavyRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Value 0..1.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public bool IsWavy { get => (bool)GetValue(IsWavyProperty); set => SetValue(IsWavyProperty, value); }
    public int WaveCount { get => (int)GetValue(WaveCountProperty); set => SetValue(WaveCountProperty, value); }
    public double Amplitude { get => (double)GetValue(AmplitudeProperty); set => SetValue(AmplitudeProperty, value); }
    public double GapDegrees { get => (double)GetValue(GapDegreesProperty); set => SetValue(GapDegreesProperty, value); }

    private void SyncDisplay()
    {
        var v = Math.Clamp(Value, 0, 1);
        if (IsLoaded && !Motion.ReduceMotion) this.Animate(DisplayProperty, v, Motion.Pick(MotionToken.SpatialDefault, MotionToken.Data));
        else this.Set(DisplayProperty, v);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var size = Math.Min(w, h);
        if (size <= 0) return;
        var th = Thickness;
        var amp = IsWavy ? Amplitude : 0;
        var c = new Point(w / 2, h / 2);
        var r = size / 2 - th / 2 - amp;
        if (r <= 1) return;
        var v = Math.Clamp((double)GetValue(DisplayProperty), 0, 1);
        var activePen = new Pen(ActiveBrush ?? Brushes.White, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var trackPen = new Pen(TrackBrush ?? Brushes.Gray, th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var capDeg = th / 2 / r * 180 / Math.PI;
        var gap = GapDegrees + 2 * capDeg;
        var sweep = v * 360;

        if (sweep < 0.5)
        {
            dc.DrawEllipse(null, trackPen, c, r, r);
            return;
        }
        if (sweep > 359.5)
        {
            dc.DrawGeometry(null, activePen, Arc(c, r, 0, 360, amp, true));
            return;
        }
        var activeEnd = sweep;
        dc.DrawGeometry(null, activePen, Arc(c, r, 0, activeEnd, amp, false));
        var trackFrom = activeEnd + gap;
        var trackTo = 360 - gap;
        if (trackTo - trackFrom > 0.5) dc.DrawGeometry(null, trackPen, Arc(c, r, trackFrom, trackTo, 0, false));
    }

    private StreamGeometry Arc(Point c, double r, double fromDeg, double toDeg, double amp, bool closed)
    {
        var waves = Math.Max(1, WaveCount);
        Point At(double deg)
        {
            var rad = deg * Math.PI / 180;
            var rr = r + amp * Math.Sin(waves * rad);
            return new Point(c.X + rr * Math.Sin(rad), c.Y - rr * Math.Cos(rad));
        }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(fromDeg), false, closed);
            if (amp <= 0)
            {
                // two half arcs avoid the 180° ambiguity of ArcTo
                var midDeg = (fromDeg + toDeg) / 2;
                ctx.ArcTo(At(midDeg), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                ctx.ArcTo(At(toDeg), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            }
            else
            {
                var n = Math.Max(2, (int)Math.Ceiling((toDeg - fromDeg) / 2.5));
                var pts = new List<Point>(n);
                for (var i = 1; i <= n; i++) pts.Add(At(fromDeg + (toDeg - fromDeg) * i / n));
                ctx.PolyLineTo(pts, true, true);
            }
        }
        g.Freeze();
        return g;
    }
}
