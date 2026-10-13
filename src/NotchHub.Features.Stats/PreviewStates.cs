using System.Windows;

namespace NotchHub.Features.Stats;

/// <summary>Extra states rendered by tools/preview (demo data only).</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement v) => v is StatsView ? ["speedtest-running"] : [];

    public static Task ApplyAsync(FrameworkElement v, string state)
    {
        if (v is StatsView { DataContext: StatsViewModel vm } && state == "speedtest-running")
        {
            vm.SpeedRunning = true;
            vm.SpeedPhaseText = NotchHub.UI.Localization.Loc.Get("stats.phaseDownload");
            vm.SpeedLive = "184";
            vm.SpeedProgress = 0.45;
        }
        return Task.Delay(600);
    }
}
