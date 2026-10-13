using System.Windows;
using NotchHub.Core.Contracts;

namespace NotchHub.Features.Clipboard;

/// <summary>Extra states rendered by tools/preview.</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement v) => new[] { "image", "link-multi", "confirm" };

    public static Task ApplyAsync(FrameworkElement v, string state)
    {
        if (v is not ClipboardView view) return Task.CompletedTask;
        var vm = view.ViewModel;
        ClipEntry? Find(ClipKind k) => vm.Entries.FirstOrDefault(e => e.Kind == k);
        switch (state)
        {
            case "image":
                if (Find(ClipKind.Image) is { } img) view.List.SelectedItem = img;
                break;
            case "link-multi":
                if (Find(ClipKind.Link) is { } link)
                {
                    view.List.SelectedItem = link;
                    if (Find(ClipKind.Code) is { } code) view.List.SelectedItems.Add(code);
                    view.List.SelectedItems.Remove(link);
                    view.List.SelectedItems.Add(link);
                }
                break;
            case "confirm":
                vm.ConfirmingClear = true;
                break;
        }
        return Task.Delay(400);
    }
}
