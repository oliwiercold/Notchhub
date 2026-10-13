using NotchHub.Demo.Imaging;

namespace NotchHub.Demo.SampleData;

/// <summary>A process group of the demo "Top apps" lists with its typical memory / CPU usage.</summary>
internal sealed record DemoProcess(string Key, string DisplayName, IconSpec Icon, double MemoryMb, double CpuPercent, int Instances);

/// <summary>
/// Running apps of the demo machine. Memory order: Chrome, VS Code, Spotify, Discord, Steam, Explorer…;
/// CPU order differs (VS Code first) so the RAM/CPU toggle visibly re-sorts.
/// </summary>
internal static class ProcessCatalog
{
    public static IReadOnlyList<DemoProcess> All { get; } =
    [
        new("chrome.exe", "Google Chrome", DemoIcons.Chrome, 1433.6, 6.4, 27),
        new("code.exe", "Visual Studio Code", DemoIcons.VsCode, 820, 7.9, 14),
        new("spotify.exe", "Spotify", DemoIcons.Spotify, 410, 1.2, 6),
        new("discord.exe", "Discord", DemoIcons.Discord, 300, 2.6, 6),
        new("steam.exe", "Steam", DemoIcons.Steam, 245, 0.5, 4),
        new("explorer.exe", "Windows Explorer", DemoIcons.Explorer, 182, 0.7, 1),
        new("msedgewebview2.exe", "Microsoft Edge WebView2", DemoIcons.WebView, 156, 0.4, 8),
        new("windowsterminal.exe", "Windows Terminal", DemoIcons.Terminal, 96, 0.3, 2),
        new("notchhub.exe", "NotchHub", DemoIcons.NotchHub, 84, 1.8, 1),
    ];

    /// <summary>Length of the TopByMemory / TopByCpu lists.</summary>
    public const int TopCount = 8;
}
