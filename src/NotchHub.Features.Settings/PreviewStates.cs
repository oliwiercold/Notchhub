using System.Windows;
using NotchHub.UI.Modding;

namespace NotchHub.Features.Settings;

/// <summary>Extra states rendered by tools/preview (demo data).</summary>
internal static class PreviewStates
{
    public static IEnumerable<string> Names(FrameworkElement v) => ["mods", "mods-guide", "meeting", "notch", "phone", "about"];

    public static async Task ApplyAsync(FrameworkElement v, string state)
    {
        if (v.DataContext is not SettingsViewModel vm) return;
        if (ModRegistry.Mods.Count == 0)
        {
            ModRegistry.Mods.Add(new ModInfo("kursy-nbp", "Kursy NBP", "1.0.0", "NotchHub", "Średnie kursy walut z tabeli A NBP.",
                ModKind.Widget, @"C:\mods\kursy-nbp", true, null));
            ModRegistry.Mods.Add(new ModInfo("pomodoro", "Pomodoro", "1.0.0", "NotchHub", "Timer 25/5 z kafelkiem i powiadomieniem.",
                ModKind.Assembly, @"C:\mods\pomodoro", true, null));
            ModRegistry.Mods.Add(new ModInfo("broken", "Zepsuty mod", null, null, null,
                ModKind.Assembly, @"C:\mods\broken", true, "Could not load file or assembly 'Broken.dll'."));
        }
        vm.Section = state switch { "meeting" => SettingsSection.MeetingBar, "notch" => SettingsSection.Notch, "phone" => SettingsSection.Phone, "about" => SettingsSection.General,_ => SettingsSection.Mods };
        vm.ShowModGuide = state == "mods-guide";
        await Task.Delay(500);
        if (state == "about" && v.FindName("Scroller") is System.Windows.Controls.ScrollViewer sv)
        {
            v.UpdateLayout();
            sv.ScrollToEnd();
            sv.UpdateLayout();
            await Task.Delay(200);
        }
    }
}
