using System.Windows;

namespace NotchHub.Features.Controls;

/// <summary>Extra states rendered by tools/preview (demo data).</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement v) => ["network", "wifi-password", "bluetooth", "audio"];

    public static async Task ApplyAsync(FrameworkElement v, string state)
    {
        if (v.DataContext is not ControlsViewModel vm) return;
        switch (state)
        {
            case "network": vm.OpenNetworkSheetCommand.Execute(null); await Task.Delay(1500); break;
            case "wifi-password":
                vm.OpenNetworkSheetCommand.Execute(null);
                await Task.Delay(1500);
                if (vm.Networks.FirstOrDefault(n => n.IsSecured && !n.Network.HasProfile && !n.IsConnected) is { } item)
                {
                    vm.ActivateNetworkCommand.Execute(item);
                    item.Failed = true;
                }
                if (vm.Networks.FirstOrDefault(n => n.Network.HasProfile && !n.IsConnected) is { } saved) saved.IsBusy = true;
                break;
            case "bluetooth": vm.OpenBluetoothSheetCommand.Execute(null); break;
            case "audio": vm.OpenAudioSheetCommand.Execute(null); break;
        }
        await Task.Delay(600);
    }
}
