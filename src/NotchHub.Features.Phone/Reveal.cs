using System.Windows;
using System.Windows.Media;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Phone;

/// <summary>Attached accordion: animates height (layout scale) + opacity when <c>IsOpen</c> changes;
/// <c>AnimateOnLoad</c> makes newly created elements grow in.</summary>
public static class Reveal
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.RegisterAttached(
        "IsOpen", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnIsOpenChanged));

    public static readonly DependencyProperty AnimateOnLoadProperty = DependencyProperty.RegisterAttached(
        "AnimateOnLoad", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnAnimateOnLoadChanged));

    public static bool GetIsOpen(DependencyObject d) => (bool)d.GetValue(IsOpenProperty);
    public static void SetIsOpen(DependencyObject d, bool v) => d.SetValue(IsOpenProperty, v);
    public static bool GetAnimateOnLoad(DependencyObject d) => (bool)d.GetValue(AnimateOnLoadProperty);
    public static void SetAnimateOnLoad(DependencyObject d, bool v) => d.SetValue(AnimateOnLoadProperty, v);

    private static ScaleTransform Scale(FrameworkElement el)
    {
        if (el.LayoutTransform is ScaleTransform st && !st.IsFrozen) return st;
        st = new ScaleTransform(1, 1);
        el.LayoutTransform = st;
        return st;
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        var open = (bool)e.NewValue;
        if (!el.IsLoaded || Motion.ReduceMotion)
        {
            el.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            Scale(el).ScaleY = open ? 1 : 0;
            el.Opacity = open ? 1 : 0;
            return;
        }
        var st = Scale(el);
        if (open)
        {
            if (el.Visibility != Visibility.Visible) { st.ScaleY = 0; el.Opacity = 0; }
            el.Visibility = Visibility.Visible;
            st.Animate(ScaleTransform.ScaleYProperty, 1, Springs.SpatialDefault);
            el.Animate(UIElement.OpacityProperty, 1, Springs.EffectsDefault);
        }
        else
        {
            st.Animate(ScaleTransform.ScaleYProperty, 0, Springs.EffectsDefault, () =>
            {
                if (!GetIsOpen(el)) el.Visibility = Visibility.Collapsed;
            });
            el.Animate(UIElement.OpacityProperty, 0, Springs.EffectsFast);
        }
    }

    private static void OnAnimateOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || !(bool)e.NewValue || Motion.ReduceMotion) return;
        el.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        el.Loaded -= OnLoaded;
        if (!GetIsOpen(el)) return;
        var st = Scale(el);
        st.ScaleY = 0;
        el.Opacity = 0;
        st.Animate(ScaleTransform.ScaleYProperty, 1, Springs.SpatialDefault);
        el.Animate(UIElement.OpacityProperty, 1, Springs.EffectsDefault);
    }
}

/// <summary>Attached "press squish": shrinks on mouse down, springs back on release.</summary>
public static class Press
{
    public static readonly DependencyProperty SquishProperty = DependencyProperty.RegisterAttached(
        "Squish", typeof(double), typeof(Press), new PropertyMetadata(0.0, OnChanged));

    public static double GetSquish(DependencyObject d) => (double)d.GetValue(SquishProperty);
    public static void SetSquish(DependencyObject d, double value) => d.SetValue(SquishProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || (double)e.OldValue > 0) return;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new ScaleTransform(1, 1);
        el.PreviewMouseLeftButtonDown += (_, _) => To(el, 1 - GetSquish(el), Springs.SpatialFast);
        el.PreviewMouseLeftButtonUp += (_, _) => To(el, 1, Springs.SpatialDefault);
        el.MouseLeave += (_, _) => To(el, 1, Springs.SpatialDefault);
    }

    private static void To(UIElement el, double s, SpringEase spring)
    {
        if (el.RenderTransform is not ScaleTransform st) return;
        st.Animate(ScaleTransform.ScaleXProperty, s, spring);
        st.Animate(ScaleTransform.ScaleYProperty, s, spring);
    }
}
