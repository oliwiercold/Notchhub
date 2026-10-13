using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NotchHub.UI;

namespace NotchHub.Features.Settings;

public partial class SettingsView : UserControl, ITabView
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
        // hotkey capture: swallow every key before the focused control sees it
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (ViewModel.HandleKey(key, Keyboard.Modifiers)) e.Handled = true;
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.CancelCapture();
        // keep the newly selected section starting at its top
        if (FindName("Scroller") is ScrollViewer sv) sv.ScrollToTop();
    }

    private static bool IsEnter(KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return false;
        e.Handled = true;
        return true;
    }

    private static void Run(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e) { if (IsEnter(e)) Run(ViewModel.ApplyCustomHexCommand); }
    private void OnIgnoredAppKeyDown(object sender, KeyEventArgs e) { if (IsEnter(e)) Run(ViewModel.AddIgnoredAppCommand); }
    private void OnCityKeyDown(object sender, KeyEventArgs e) { if (IsEnter(e)) Run(ViewModel.SearchCityCommand); }
    private void OnCalUrlKeyDown(object sender, KeyEventArgs e) { if (IsEnter(e)) Run(ViewModel.AddSubscriptionCommand); }
    private void OnDeviceNameKeyDown(object sender, KeyEventArgs e) { if (IsEnter(e)) Run(ViewModel.CommitDeviceNameCommand); }
    private void OnDeviceNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Run(ViewModel.CommitDeviceNameCommand);
    private void OnMutedAppKeyDown(object sender, KeyEventArgs e) { if (IsEnter(e)) Run(ViewModel.AddMutedAppCommand); }

    private void OnLocationPicked(object sender, SelectionChangedEventArgs e)
    {
        if (sender is Selector { SelectedItem: LocationResult item }) Run(item.PickCommand);
    }
}
