using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotchHub.UI;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Today;

public partial class TodayView : UserControl, ITabView
{
    public TodayView()
    {
        InitializeComponent();
        DataContext = new TodayViewModel();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TodayViewModel.IsEventSheetOpen) && ViewModel.IsEventSheetOpen)
                Dispatcher.BeginInvoke(() => EventTitleBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            else if (e.PropertyName == nameof(TodayViewModel.IsDayExpanded))
                AnimateDay(ViewModel.IsDayExpanded);
        };
        PreviewKeyDown += OnPreviewKey;
    }

    // ── expanded day agenda ──

    private const double CompactEventsHeight = 58;
    private const double RowGap = 10;
    private double _expandedHeight;

    /// <summary>Grows the events card over the timer card's space (the timer fades out), or folds it back.</summary>
    private void AnimateDay(bool expanded)
    {
        if (expanded && TimerCard.Opacity > 0.99 && TimerCard.ActualHeight > 0)
            _expandedHeight = CompactEventsHeight + TimerCard.ActualHeight + RowGap;
        var target = expanded ? Math.Max(_expandedHeight, CompactEventsHeight * 3) : CompactEventsHeight;
        TimerCard.IsHitTestVisible = !expanded;
        if (!IsVisible)
        {
            EventsBox.Set(HeightProperty, target);
            TimerCard.Set(OpacityProperty, expanded ? 0 : 1);
            DayAgenda.Set(OpacityProperty, 1);
            return;
        }
        EventsBox.Animate(HeightProperty, target, expanded ? MotionToken.Grow : MotionToken.Shrink);
        TimerCard.Animate(OpacityProperty, expanded ? 0 : 1, expanded ? MotionToken.FxFast : MotionToken.FxSlow);
        if (expanded)
        {
            DayAgenda.Set(OpacityProperty, 0);
            DayAgenda.Animate(OpacityProperty, 1, MotionToken.Fx);
        }
    }

    private void OnCompactEventsClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        ViewModel.ToggleDayCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !ViewModel.IsDayExpanded || ViewModel.IsEventSheetOpen) return;
        ViewModel.CollapseDayCommand.Execute(null);
        e.Handled = true;
    }

    public TodayViewModel ViewModel => (TodayViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();

    // ── timer fields: wheel / arrows adjust ──

    private void OnTimerFieldWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel.AdjustTimerField(IsHours(sender), e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnTimerFieldKey(object sender, KeyEventArgs e)
    {
        var d = e.Key switch { Key.Up => 1, Key.Down => -1, Key.PageUp => 5, Key.PageDown => -5, _ => 0 };
        if (d == 0) return;
        ViewModel.AdjustTimerField(IsHours(sender), d);
        if (sender is TextBox tb) tb.CaretIndex = tb.Text.Length;
        e.Handled = true;
    }

    private static bool IsHours(object sender) => sender is FrameworkElement { Tag: "h" };

    // ── events: right-click reveals delete for local events ──

    private void OnEventRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EventItem item } && item.CanDelete)
        {
            item.ShowDelete = !item.ShowDelete;
            e.Handled = true;
        }
    }

    // ── to-do inline editing ──

    private void OnTodoTextMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: TodoVm vm } && !vm.IsEditing)
        {
            vm.BeginEdit();
            e.Handled = true;
        }
    }

    private void OnEditBoxVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox tb)
            Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnEditBoxKey(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TodoVm vm }) return;
        if (e.Key == Key.Enter) { vm.CommitEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CancelEdit(); e.Handled = true; }
    }

    private void OnEditBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoVm vm }) vm.CommitEdit();
    }
}
