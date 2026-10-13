using System.Windows;
using NotchHub.Core;

namespace NotchHub.Features.Today;

/// <summary>Extra states rendered by tools/preview (found by reflection).</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement v) => v is TodayView ? ["day-expanded", "running-sheet"] : [];

    public static async Task ApplyAsync(FrameworkElement v, string state)
    {
        if (v is not TodayView view) return;
        if (state == "day-expanded")
        {
            view.ViewModel.SelectDayCommand.Execute(view.ViewModel.Days.FirstOrDefault(d => d.IsToday) ?? view.ViewModel.Days.FirstOrDefault());
            await Task.Delay(1200);
            return;
        }
        if (state != "running-sheet") return;
        view.ViewModel.CollapseDayCommand.Execute(null);
        AppServices.Timer.Start(TimeSpan.FromMinutes(5));
        view.ViewModel.OnHidden();
        view.ViewModel.OnShown();
        view.ViewModel.OpenEventSheetCommand.Execute(null);
        view.ViewModel.NewTitle = "Spotkanie";
        await Task.Delay(900);
    }
}
