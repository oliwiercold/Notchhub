using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Power;

/// <summary>Attached "press squish": the element shrinks on mouse down and springs back on release.</summary>
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
        el.LostMouseCapture += (_, _) => To(el, 1, Springs.SpatialDefault);
    }

    private static void To(UIElement el, double s, SpringEase spring)
    {
        if (el.RenderTransform is not ScaleTransform st) return;
        st.Animate(ScaleTransform.ScaleXProperty, s, spring);
        st.Animate(ScaleTransform.ScaleYProperty, s, spring);
    }
}
