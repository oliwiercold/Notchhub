using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using NotchHub.UI.Controls;

namespace NotchHub.Features.Controls;

/// <summary>(Failed, IsPasswordOpen) → the stand-alone error line is visible only when there is no password field.</summary>
public sealed class FailedWithoutPassword : System.Windows.Markup.MarkupExtension, IMultiValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [true, false] ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Inline Wi-Fi password entry (PasswordBox cannot bind its Password — kept in the view, handed to the VM on connect).</summary>
public partial class ControlsView
{
    private bool _pwSyncing;

    /// <summary>The row root (StackPanel of one network) and the named parts inside it.</summary>
    private static (WifiItemViewModel? Item, PasswordBox? Pw, TextBox? Text, IconButton? Eye) Parts(object sender)
    {
        if (sender is not FrameworkElement { DataContext: WifiItemViewModel item } fe) return default;
        var root = fe;
        while (root.Parent is FrameworkElement p && ReferenceEquals(p.DataContext, item)) root = p;
        return (item, Find<PasswordBox>(root, "Pw"), Find<TextBox>(root, "PwText"), Find<IconButton>(root, "Eye"));
    }

    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            if (d is T t && t.Name == name) return t;
            if (Find<T>(d, name) is { } found) return found;
        }
        return null;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var pw = (PasswordBox)sender;
        pw.Tag = pw.Password.Length == 0 ? null : "1";
        if (_pwSyncing) return;
        var (_, _, text, _) = Parts(sender);
        if (text is null) return;
        _pwSyncing = true;
        text.Text = pw.Password;
        _pwSyncing = false;
    }

    private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_pwSyncing) return;
        var (_, pw, text, _) = Parts(sender);
        if (pw is null || text is null) return;
        _pwSyncing = true;
        pw.Password = text.Text;
        pw.Tag = text.Text.Length == 0 ? null : "1";
        _pwSyncing = false;
    }

    private void OnTogglePasswordVisible(object sender, RoutedEventArgs e)
    {
        var (_, pw, text, eye) = Parts(sender);
        if (pw is null || text is null || eye is null) return;
        var show = text.Visibility != Visibility.Visible;
        text.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        pw.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        eye.Icon = show ? "visibility_off" : "visibility";
        FocusLater(show ? text : pw);
        if (show) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => text.CaretIndex = text.Text.Length);
    }

    private void OnPasswordVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var (item, pw, text, eye) = Parts(sender);
        if (item is null || pw is null) return;
        if (item.IsPasswordOpen)
        {
            if (pw.IsVisible)
            {
                // The notch window does not take focus by itself — activate it so the field can be typed into.
                Window.GetWindow(pw)?.Activate();
                FocusLater(pw);
            }
            return;
        }
        // Field closed (connected / dismissed): forget the typed password and reset show/hide.
        pw.Clear();
        if (text is not null) { text.Clear(); text.Visibility = Visibility.Collapsed; }
        pw.Visibility = Visibility.Visible;
        if (eye is not null) eye.Icon = "visibility";
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Connect(sender);
    }

    private void OnWifiConnectClick(object sender, RoutedEventArgs e) => Connect(sender);

    private void Connect(object sender)
    {
        var (item, pw, _, _) = Parts(sender);
        if (item is null || pw is null) return;
        ViewModel.ConnectWithPassword(item, pw.Password);
    }

    private void FocusLater(IInputElement target) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { target.Focus(); Keyboard.Focus(target); });
}
