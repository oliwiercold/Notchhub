using System.Windows;
using NotchHub.Core.Contracts;

namespace NotchHub.Features.Power;

/// <summary>Extra states for tools/preview.</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement view) => view is PowerView ? ["armed"] : [];

    public static Task ApplyAsync(FrameworkElement view, string state)
    {
        if (view is PowerView pv && state == "armed")
        {
            var tile = pv.ViewModel.Tiles.FirstOrDefault(t => t.Action == PowerAction.Restart);
            if (tile is not null) pv.ViewModel.ActivateCommand.Execute(tile);
        }
        return Task.CompletedTask;
    }
}
