namespace NotchHub.Demo.SampleData;

/// <summary>A visible Wi-Fi network in the demo neighbourhood.</summary>
internal sealed record DemoWifi(string Ssid, int SignalPercent, bool IsSecured, bool HasProfile, string Band);

/// <summary>Wi-Fi networks returned by the demo scan (strongest first, as the contract requires).</summary>
internal static class WifiCatalog
{
    public const string HomeSsid = "Home 5G";

    public static IReadOnlyList<DemoWifi> Visible { get; } =
    [
        new(HomeSsid, 92, true, true, "5 GHz"),
        new("Home 2.4", 81, true, true, "2.4 GHz"),
        new("Orange_Swiatlowod_7A21", 63, true, false, "5 GHz"),
        new("PLAY_Internet_4G_B3F0", 47, true, false, "2.4 GHz"),
        new("Kawiarnia Ziarno", 36, false, false, "2.4 GHz"),
        new("DIRECT-5C-HP LaserJet", 21, true, false, "2.4 GHz"),
    ];

    /// <summary>Signal bars (0..4) for a signal percentage, like the Windows tray icon.</summary>
    public static int Bars(int percent) => percent switch { >= 80 => 4, >= 60 => 3, >= 40 => 2, > 0 => 1, _ => 0 };
}
