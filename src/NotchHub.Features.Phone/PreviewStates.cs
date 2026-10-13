using System.Windows;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Features.Phone;

/// <summary>Extra states for tools/preview (demo data covers "paired online").</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement view) => view is PhoneView
        ? ["expanded", "disabled", "setup", "discovered", "waiting", "incoming", "offline", "lowbattery", "call", "ringing", "missed", "nobattery", "reconnecting", "swipe"]
        : [];

    public static async Task ApplyAsync(FrameworkElement view, string state)
    {
        if (view is not PhoneView pv) return;
        var vm = pv.ViewModel;
        var s = AppServices.Phone;
        var local = s.LocalDeviceName;
        var pixel = new PhoneDevice("p1", "Pixel 8 Pro", "phone", true, PhonePairState.NotPaired, "192.168.1.42", "3f9a21c7", 8);
        var tab = new PhoneDevice("t1", "Galaxy Tab S9", "tablet", true, PhonePairState.NotPaired, "192.168.1.57", null, 8);
        PhoneSnapshot Live() => new(true, true, local, s.Devices, s.ActiveDevice, s.Battery, s.Signal, s.Notifications, s.ActiveCall);

        switch (state)
        {
            case "expanded":
                if (vm.Notifications.FirstOrDefault(n => n.CanReply) is { } item) item.ToggleCommand.Execute(null);
                break;
            case "disabled":
                vm.ApplyOverride(new(false, false, local, [], null, null, null, []));
                break;
            case "setup":
                vm.ApplyOverride(new(true, true, local, [], null, null, null, []));
                break;
            case "discovered":
                vm.ApplyOverride(new(true, true, local, [pixel, tab], null, null, null, []));
                break;
            case "waiting":
                vm.ApplyOverride(new(true, true, local, [pixel with { PairState = PhonePairState.RequestedByUs }], null, null, null, []));
                break;
            case "incoming":
                vm.ApplyOverride(new(true, true, local, [pixel with { PairState = PhonePairState.RequestedByPeer }], null, null, null, []));
                break;
            case "offline":
                var off = pixel with { PairState = PhonePairState.Paired, IsReachable = false };
                vm.ApplyOverride(new(true, true, local, [off], off, null, null, []));
                break;
            case "lowbattery":
                var live = Live();
                vm.ApplyOverride(live with { Battery = new PhoneBattery(12, false, true), Signal = new PhoneSignal("LTE", 1) });
                break;
            case "call":
                vm.ApplyOverride(Live() with { Call = new PhoneCall(PhoneCallState.Talking, "Mama", "+48 600 100 200", DateTimeOffset.Now.AddSeconds(-151), null) });
                break;
            case "ringing":
                vm.ApplyOverride(Live() with { Call = new PhoneCall(PhoneCallState.Ringing, "+48 22 123 45 67", "+48 22 123 45 67", DateTimeOffset.Now, null) });
                break;
            case "missed":
                vm.ApplyOverride(Live() with { Call = new PhoneCall(PhoneCallState.Missed, "Arsh", "+48 512 345 678", DateTimeOffset.Now.AddMinutes(-1), null) });
                break;
            case "nobattery":
                vm.ApplyOverride(Live() with { Battery = null });
                break;
            case "reconnecting":
                var away = s.ActiveDevice! with { IsReachable = false };
                vm.ApplyOverride(new(true, true, local, [away], away, s.Battery, null, []));
                break;
            case "swipe":
                // a row mid-drag (as the user sees it while swiping)
                vm.ApplyOverride(Live());
                await Task.Delay(600);
                if (FindRow(pv) is { } row)
                {
                    row.RenderTransform = new System.Windows.Media.TranslateTransform(row.ActualWidth * 0.3, 0);
                    if (row is System.Windows.Controls.Border { Child: { } c }) c.Opacity = 0.58;
                }
                break;
        }
        await Task.Delay(300);
    }

    private static FrameworkElement? FindRow(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Border b && b.DataContext is NotificationItem) return b;
            if (FindRow(child) is { } r) return r;
        }
        return null;
    }
}
