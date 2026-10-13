using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NotchHub.UI;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Compact;

public partial class LiveActivityView : UserControl, ITabView
{
    private const double Flat = 0.22;
    private readonly ScaleTransform[] _bars;
    private readonly Random _rng = new();

    public LiveActivityView()
    {
        InitializeComponent();
        DataContext = new LiveActivityViewModel();
        _bars = Bars.Children.OfType<FrameworkElement>().Select(b =>
        {
            var st = new ScaleTransform(1, Flat);
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            b.RenderTransform = st;
            return st;
        }).ToArray();
        ViewModel.PropertyChanged += OnVmChanged;
        UpdateAnimation();
    }

    public LiveActivityViewModel ViewModel => (LiveActivityViewModel)DataContext;

    public void OnShown() { ViewModel.OnShown(); UpdateAnimation(); }
    public void OnHidden() { ViewModel.OnHidden(); UpdateAnimation(); }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveActivityViewModel.IsPlaying) or nameof(LiveActivityViewModel.IsShown) or nameof(LiveActivityViewModel.ShowMedia)) UpdateAnimation();
    }

    /// <summary>Bars bounce only while playing AND visible; otherwise they settle flat and nothing ticks.
    /// The bounce is a pre-baked looping key-frame animation capped at a low frame rate: the notch is a layered window, so every
    /// rendered frame costs a surface read-back — a tiny equalizer must not drive the whole window at the monitor refresh rate.</summary>
    private void UpdateAnimation()
    {
        var run = ViewModel.IsPlaying && ViewModel.ShowMedia && ViewModel.IsShown && !Motion.ReduceMotion;
        if (run)
        {
            if (_running) return;
            _running = true;
            for (var i = 0; i < _bars.Length; i++) _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, BuildLoop(i));
            return;
        }
        if (!_running && !ViewModel.IsPlaying) return;
        _running = false;
        var target = ViewModel.IsPlaying ? 0.55 : Flat;
        foreach (var st in _bars) st.Animate(ScaleTransform.ScaleYProperty, target, Springs.SpatialDefault);
    }

    private bool _running;

    private DoubleAnimationUsingKeyFrames BuildLoop(int bar)
    {
        // Outer bars a bit shorter on average: reads as an equalizer rather than noise.
        var max = bar is 1 or 2 ? 1.0 : 0.8;
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(anim, 24);
        var t = 0.0;
        var first = 0.25 + _rng.NextDouble() * (max - 0.25);
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(first, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        for (var k = 0; k < 9; k++)
        {
            t += 0.16 + _rng.NextDouble() * 0.12;
            var h = k == 8 ? first : 0.25 + _rng.NextDouble() * (max - 0.25);
            anim.KeyFrames.Add(new SplineDoubleKeyFrame(h, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)), new KeySpline(0.3, 0, 0.2, 1)));
        }
        return anim;
    }
}
