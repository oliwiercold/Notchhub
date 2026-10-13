using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;

namespace NotchHub.Core;

/// <summary>
/// Service locator populated once by the composition root (NotchHub.App) before any view is created.
/// View models and services read their dependencies from here at runtime.
/// In demo mode (--demo) the same properties hold fake implementations from NotchHub.Demo.
/// </summary>
public static class AppServices
{
    public static bool IsDemo { get; set; }

    public static ISettingsService Settings { get; set; } = null!;
    public static IUiDispatcher Ui { get; set; } = null!;
    /// <summary>Set when the notch window exists; null during early startup.</summary>
    public static IShell? Shell { get; set; }

    public static IMediaService Media { get; set; } = null!;
    public static IAudioService Audio { get; set; } = null!;
    public static INetworkService Network { get; set; } = null!;
    public static IBluetoothService Bluetooth { get; set; } = null!;
    public static IDisplayService Display { get; set; } = null!;
    public static IStatsService Stats { get; set; } = null!;
    public static ISpeedTestService SpeedTest { get; set; } = null!;
    public static IPowerService Power { get; set; } = null!;
    public static IClipboardService Clipboard { get; set; } = null!;
    public static IForegroundService Foreground { get; set; } = null!;
    public static IHotkeyService Hotkeys { get; set; } = null!;
    public static IWallpaperService Wallpaper { get; set; } = null!;
    public static IAutostartService Autostart { get; set; } = null!;
    public static IWeatherService Weather { get; set; } = null!;
    public static ICalendarService Calendar { get; set; } = null!;
    public static ITodoService Todos { get; set; } = null!;
    public static ITimerService Timer { get; set; } = null!;
    public static IPhoneService Phone { get; set; } = null!;

    /// <summary>Every registered service that needs StartAsync / Dispose, in start order.</summary>
    public static IEnumerable<IBackgroundService> BackgroundServices()
    {
        IBackgroundService?[] all =
        [
            Foreground, Hotkeys, Wallpaper, Media, Audio, Network, Bluetooth, Display, Stats, SpeedTest,
            Clipboard, Weather, Calendar, Todos, Timer, Phone,
        ];
        return all.Where(s => s is not null)!;
    }
}
