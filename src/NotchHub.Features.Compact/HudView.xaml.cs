using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NotchHub.Core.Contracts;
using NotchHub.UI;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Compact;

public partial class HudView : UserControl, ITabView
{
    public HudView()
    {
        InitializeComponent();
        DataContext = new HudViewModel();
        ViewModel.Presented += OnPresented;
    }

    public HudViewModel ViewModel => (HudViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();

    public void OnHidden()
    {
        ViewModel.OnHidden();
        StopRing();
    }

    private void OnPresented(object? sender, bool kindChanged)
    {
        var reduce = Motion.ReduceMotion;
        if (kindChanged && IsVisible && !reduce)
        {
            // Kind switch while visible: content drops in from above with a springy settle.
            Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            Spring(ContentShift, TranslateTransform.YProperty, -8, 0);
            Spring(ContentScale, ScaleTransform.ScaleXProperty, 0.96, 1);
            Spring(ContentScale, ScaleTransform.ScaleYProperty, 0.96, 1);
        }
        if (ViewModel.Kind == HudKind.TimerFinished && !reduce)
        {
            // Ringing wiggle, a bounded number of times (no endless animation).
            var wiggle = new DoubleAnimation(-16, 16, TimeSpan.FromMilliseconds(90))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(8),
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            TimerRotate.BeginAnimation(RotateTransform.AngleProperty, wiggle);
        }

        if (ViewModel.Kind == HudKind.PhoneCharging) AnimateCharge(reduce);

        if (ViewModel.Kind == HudKind.PhoneCall && ViewModel.IsRinging && !reduce) StartRing();
        else StopRing();
    }

    /// <summary>Battery fill grows to the percentage (from a bit lower when plugged in, like a charge "pour"); the bolt pops in.</summary>
    private void AnimateCharge(bool reduce)
    {
        var target = Math.Max(0.04, ViewModel.ChargePercent / 100.0);
        if (reduce)
        {
            ChargeFillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ChargeFillScale.ScaleX = target;
            return;
        }
        var from = ViewModel.IsCharging ? Math.Max(0.04, target - 0.3) : Math.Min(1, target + 0.08);
        var fill = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        ChargeFillScale.ScaleX = from;
        ChargeFillScale.BeginAnimation(ScaleTransform.ScaleXProperty, fill);
        Spring(BoltScale, ScaleTransform.ScaleXProperty, 0.3, 1);
        Spring(BoltScale, ScaleTransform.ScaleYProperty, 0.3, 1);
    }

    /// <summary>Pulsing ring around the caller while the phone rings — bounded (~30 s), stopped when hidden or answered.</summary>
    private void StartRing()
    {
        var duration = TimeSpan.FromMilliseconds(1200);
        var repeat = new RepeatBehavior(25);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scale = new DoubleAnimation(0.9, 1.4, duration) { RepeatBehavior = repeat, EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        CallRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        CallRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        CallRing.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.9, 0, duration) { RepeatBehavior = repeat, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    private void StopRing()
    {
        CallRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CallRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CallRing.BeginAnimation(OpacityProperty, null);
    }

    private static void Spring(Animatable target, DependencyProperty dp, double from, double to)
    {
        var a = Motion.To(to, Springs.SpatialFast);
        a.From = from;
        target.BeginAnimation(dp, a);
    }

    private void OnDragStarted(object sender, RoutedEventArgs e) => ViewModel.BeginDrag();
    private void OnDragCompleted(object sender, RoutedEventArgs e) => ViewModel.EndDrag();
}
