using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotchHub.UI;

namespace NotchHub.Features.Clipboard;

public partial class ClipboardView : UserControl, ITabView
{
    private bool _restoring;
    private ClipEntry? _anchor;
    private ClipEntry? _pendingPrimary;

    public ClipboardView()
    {
        InitializeComponent();
        var vm = new ClipboardViewModel();
        DataContext = vm;

        var view = new ListCollectionView(vm.Entries);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ClipEntry.Group)));
        List.ItemsSource = view;

        vm.SelectionRestore += OnSelectionRestore;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
    }

    public ClipboardViewModel ViewModel => (ClipboardViewModel)DataContext;

    public void OnShown()
    {
        ViewModel.OnShown();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsVisible && !IsKeyboardFocusWithin) SearchBox.Focus();
        });
    }

    public void OnHidden() => ViewModel.OnHidden();

    // ───────── selection ─────────

    private void OnSelectionRestore(IReadOnlyList<ClipEntry> selected, ClipEntry? primary)
    {
        _restoring = true;
        try
        {
            List.SelectedItems.Clear();
            foreach (var e in selected) List.SelectedItems.Add(e);
        }
        finally { _restoring = false; }
        _anchor = primary;
        ViewModel.SetSelection(selected, primary);
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoring || ViewModel.IsRebuilding) return;
        var selected = List.SelectedItems.Cast<ClipEntry>().ToList();
        ClipEntry? primary;
        if (_pendingPrimary is not null && selected.Contains(_pendingPrimary)) primary = _pendingPrimary;
        else if (e.AddedItems.Count > 0) primary = e.AddedItems[^1] as ClipEntry;
        else if (ViewModel.Current is { } cur && selected.Contains(cur)) primary = cur;
        else primary = List.SelectedItem as ClipEntry;

        if (_pendingPrimary is null && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) _anchor = primary;
        ViewModel.SetSelection(selected, primary);
    }

    private void MoveSelection(int delta, bool extend)
    {
        var items = List.Items;
        if (items.Count == 0) return;
        var cur = ViewModel.Current;
        var idx = cur is null ? -1 : items.IndexOf(cur);
        var next = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, items.Count - 1);
        if (items[next] is not ClipEntry target) return;

        _pendingPrimary = target;
        try
        {
            if (extend)
            {
                var anchorIdx = _anchor is null ? -1 : items.IndexOf(_anchor);
                if (anchorIdx < 0) { _anchor = cur ?? target; anchorIdx = items.IndexOf(_anchor); }
                var (a, b) = anchorIdx <= next ? (anchorIdx, next) : (next, anchorIdx);
                _restoring = true;
                try
                {
                    List.SelectedItems.Clear();
                    for (var i = a; i <= b; i++) List.SelectedItems.Add(items[i]);
                }
                finally { _restoring = false; }
                ViewModel.SetSelection(List.SelectedItems.Cast<ClipEntry>().ToList(), target);
            }
            else
            {
                _anchor = target;
                List.SelectedItem = target;
                if (List.SelectedItems.Count > 1)
                {
                    List.SelectedItems.Clear();
                    List.SelectedItems.Add(target);
                }
                ViewModel.SetSelection(new[] { target }, target);
            }
        }
        finally { _pendingPrimary = null; }

        List.ScrollIntoView(target);
        if (List.IsKeyboardFocusWithin && List.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem lbi) lbi.Focus();
    }

    // ───────── keyboard ─────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var ctrl = (mods & ModifierKeys.Control) != 0;
        var shift = (mods & ModifierKeys.Shift) != 0;
        var alt = (mods & ModifierKeys.Alt) != 0;
        if (alt) return;
        var inSearch = SearchBox.IsKeyboardFocusWithin;
        var vm = ViewModel;

        switch (e.Key)
        {
            case Key.Up or Key.Down when !ctrl:
                MoveSelection(e.Key == Key.Up ? -1 : 1, shift);
                e.Handled = true;
                break;
            case Key.Enter when !ctrl && !shift:
                if (e.OriginalSource is ButtonBase) return;
                if (vm.ConfirmingClear) { vm.ConfirmClearCommand.Execute(null); e.Handled = true; return; }
                vm.PasteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C when ctrl && !shift:
                if (Keyboard.FocusedElement is TextBox { SelectionLength: > 0 }) return; // copy the selected text instead
                vm.CopyCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P when ctrl && !shift:
                vm.TogglePinCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete when !ctrl && !shift:
                if (inSearch && SearchBox.Text.Length > 0) return; // the search box uses Del itself
                vm.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Tab when !ctrl:
                vm.CycleFilter(shift ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Escape:
                if (vm.ConfirmingClear) { vm.ConfirmingClear = false; e.Handled = true; }
                else if (vm.Query.Length > 0) { vm.Query = ""; e.Handled = true; }
                break;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox.IsKeyboardFocusWithin || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.Control) return;
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.SelectedText = e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    // ───────── misc handlers ─────────

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != List; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase) return;
            if (d is ListBoxItem { DataContext: ClipEntry entry })
            {
                ViewModel.PasteCommand.Execute(entry);
                e.Handled = true;
                return;
            }
        }
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        // clicking the active chip must not uncheck it — resync from the view model
        if (sender is ToggleButton tb) BindingOperations.GetBindingExpression(tb, ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    private void RoundImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var g = new RectangleGeometry(new Rect(e.NewSize), 14, 14);
            g.Freeze();
            fe.Clip = g;
        }
    }
}
