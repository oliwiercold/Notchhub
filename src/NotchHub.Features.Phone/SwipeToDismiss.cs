using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Phone;

/// <summary>
/// Attached "swipe sideways to dismiss" for a list row (mouse drag, Android-notification style):
/// the row follows the pointer 1:1 and its content fades with distance; released past ~40 % of the width (or flicked)
/// it flies out and runs <c>Command</c>, otherwise it springs back. Rows with <c>CanDismiss=False</c> resist (rubber band).
/// A press without movement inside an element marked <c>TapArea=True</c> runs <c>TapCommand</c> instead (so a click
/// on the header still expands the row). Presses on buttons / text boxes are left alone.
/// </summary>
public static class SwipeToDismiss
{
    private const double StartThreshold = 6;
    private const double DismissFraction = 0.4;
    private const double FlickVelocity = 1.1; // px / ms

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(SwipeToDismiss), new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty TapCommandProperty = DependencyProperty.RegisterAttached(
        "TapCommand", typeof(ICommand), typeof(SwipeToDismiss), new PropertyMetadata(null));

    public static readonly DependencyProperty CanDismissProperty = DependencyProperty.RegisterAttached(
        "CanDismiss", typeof(bool), typeof(SwipeToDismiss), new PropertyMetadata(true));

    public static readonly DependencyProperty TapAreaProperty = DependencyProperty.RegisterAttached(
        "TapArea", typeof(bool), typeof(SwipeToDismiss), new PropertyMetadata(false));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(DragState), typeof(SwipeToDismiss), new PropertyMetadata(null));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? v) => d.SetValue(CommandProperty, v);
    public static ICommand? GetTapCommand(DependencyObject d) => (ICommand?)d.GetValue(TapCommandProperty);
    public static void SetTapCommand(DependencyObject d, ICommand? v) => d.SetValue(TapCommandProperty, v);
    public static bool GetCanDismiss(DependencyObject d) => (bool)d.GetValue(CanDismissProperty);
    public static void SetCanDismiss(DependencyObject d, bool v) => d.SetValue(CanDismissProperty, v);
    public static bool GetTapArea(DependencyObject d) => (bool)d.GetValue(TapAreaProperty);
    public static void SetTapArea(DependencyObject d, bool v) => d.SetValue(TapAreaProperty, v);

    private sealed class DragState
    {
        public bool Pressed;
        public bool Dragging;
        public bool Done;
        public bool InTapArea;
        public Point Start;
        public double Offset;
        public double LastX;
        public long LastTicks;
        public double Velocity;
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || el.GetValue(StateProperty) is not null) return;
        el.SetValue(StateProperty, new DragState());
        el.PreviewMouseLeftButtonDown += OnDown;
        el.PreviewMouseMove += OnMove;
        el.PreviewMouseLeftButtonUp += OnUp;
        el.LostMouseCapture += OnLostCapture;
    }

    private static DragState S(object sender) => (DragState)((DependencyObject)sender).GetValue(StateProperty);

    private static TranslateTransform Translate(FrameworkElement el)
    {
        if (el.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
        t = new TranslateTransform();
        el.RenderTransform = t;
        return t;
    }

    /// <summary>The faded part: the row's child (the row's own opacity belongs to <see cref="Reveal"/>).</summary>
    private static UIElement? Content(FrameworkElement el) => el is Border { Child: { } c } ? c : null;

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var el = (FrameworkElement)sender;
        var s = S(sender);
        if (s.Done || s.Dragging) return;
        s.Pressed = false;
        s.InTapArea = false;
        for (var o = e.OriginalSource as DependencyObject; o is not null && o != el; o = Parent(o))
        {
            if (o is ButtonBase or TextBoxBase or PasswordBox or ScrollBar) return;
            if (GetTapArea(o)) s.InTapArea = true;
        }
        s.Pressed = true;
        s.Start = e.GetPosition(el);
        s.LastX = s.Start.X;
        s.LastTicks = Environment.TickCount64;
        s.Velocity = 0;
        s.Offset = 0;
    }

    private static DependencyObject? Parent(DependencyObject o) =>
        o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var el = (FrameworkElement)sender;
        var s = S(sender);
        if (!s.Pressed) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(el, s, cancelled: true); return; }
        var p = e.GetPosition(el);
        // GetPosition is relative to the (translated) element; compensate so the pointer delta is in parent space
        var dx = p.X + s.Offset - s.Start.X;
        var dy = p.Y - s.Start.Y;
        if (!s.Dragging)
        {
            if (Math.Abs(dx) < StartThreshold || Math.Abs(dx) < Math.Abs(dy) * 1.2)
            {
                if (Math.Abs(dy) > StartThreshold * 2) s.Pressed = false; // vertical gesture: not ours
                return;
            }
            s.Dragging = true;
            Translate(el).BeginAnimation(TranslateTransform.XProperty, null);
            Content(el)?.BeginAnimation(UIElement.OpacityProperty, null);
            el.CaptureMouse();
        }
        var now = Environment.TickCount64;
        var x = s.Start.X + dx;
        if (now > s.LastTicks)
        {
            var v = (x - s.LastX) / (now - s.LastTicks);
            s.Velocity = s.Velocity * 0.5 + v * 0.5;
            s.LastX = x;
            s.LastTicks = now;
        }
        Apply(el, s, GetCanDismiss(el) ? dx : RubberBand(dx, Math.Max(1, el.ActualWidth)));
        e.Handled = true;
    }

    private static double RubberBand(double dx, double width)
    {
        var limit = width * 0.12;
        return Math.Sign(dx) * limit * (1 - Math.Exp(-Math.Abs(dx) / limit));
    }

    private static void Apply(FrameworkElement el, DragState s, double offset)
    {
        s.Offset = offset;
        Translate(el).X = offset;
        if (Content(el) is { } c && GetCanDismiss(el))
            c.Opacity = Math.Clamp(1 - Math.Abs(offset) / Math.Max(1, el.ActualWidth) * 1.4, 0.15, 1);
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var el = (FrameworkElement)sender;
        var s = S(sender);
        if (!s.Pressed) return;
        if (s.Dragging)
        {
            e.Handled = true;
            EndDrag(el, s, cancelled: false);
            return;
        }
        s.Pressed = false;
        if (s.InTapArea && GetTapCommand(el) is { } tap && tap.CanExecute(null))
        {
            tap.Execute(null);
            e.Handled = true;
        }
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        var s = S(sender);
        if (s.Dragging) EndDrag((FrameworkElement)sender, s, cancelled: true);
    }

    private static void EndDrag(FrameworkElement el, DragState s, bool cancelled)
    {
        var wasDragging = s.Dragging;
        s.Pressed = false;
        s.Dragging = false;
        if (el.IsMouseCaptured) el.ReleaseMouseCapture();
        if (!wasDragging) return;

        var width = Math.Max(1, el.ActualWidth);
        var flick = Math.Abs(s.Velocity) > FlickVelocity && Math.Sign(s.Velocity) == Math.Sign(s.Offset) && Math.Abs(s.Offset) > width * 0.12;
        var dismiss = !cancelled && GetCanDismiss(el) && (Math.Abs(s.Offset) > width * DismissFraction || flick);
        var t = Translate(el);
        var content = Content(el);
        if (dismiss)
        {
            s.Done = true;
            var dir = s.Offset == 0 ? 1 : Math.Sign(s.Offset);
            void Fire()
            {
                var cmd = GetCommand(el);
                if (cmd?.CanExecute(null) == true) cmd.Execute(null);
            }
            if (Motion.ReduceMotion) { t.X = dir * width * 1.1; Fire(); return; }
            content?.Animate(UIElement.OpacityProperty, 0, Springs.EffectsFast);
            t.Animate(TranslateTransform.XProperty, dir * width * 1.1, Springs.SpatialFast, Fire);
        }
        else
        {
            s.Offset = 0;
            if (Motion.ReduceMotion) { t.BeginAnimation(TranslateTransform.XProperty, null); t.X = 0; if (content is not null) content.Opacity = 1; return; }
            t.Animate(TranslateTransform.XProperty, 0, Springs.SpatialDefault);
            content?.Animate(UIElement.OpacityProperty, 1, Springs.EffectsDefault);
        }
    }
}
