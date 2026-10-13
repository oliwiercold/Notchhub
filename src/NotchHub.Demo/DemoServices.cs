using NotchHub.Core;
using NotchHub.Demo.Fakes;
using NotchHub.Demo.Imaging;

namespace NotchHub.Demo;

/// <summary>Installs fake implementations of every service into AppServices (used with --demo and for screenshots).</summary>
/// <remarks>
/// The fakes never touch the real machine (no radios, audio endpoints, clipboard, power actions or network): every
/// operation works on in-memory state and raises the same events as the real services. Sample data is realistic,
/// deterministic (seeded) and matches the reference screenshots. Generated images (album art, icons, wallpaper,
/// clipboard pictures) are rendered once on a background STA thread and cached (files under <c>AppPaths.Dir("demo")</c>).
/// </remarks>
public static class DemoServices
{
    private static volatile bool _liveEvents = true;

    /// <summary>
    /// When true (default) the fakes simulate a living system: media position advances, stats jitter, the phone posts
    /// notifications, the volume changes externally now and then… When false nothing changes spontaneously (screenshots);
    /// user-initiated operations (toggles, timer, speed test…) keep working. Can be changed at any time.
    /// </summary>
    public static bool LiveEvents
    {
        get => _liveEvents;
        set => _liveEvents = value;
    }

    /// <summary>Sets every AppServices.* service property (except Settings, Ui, Shell) to a fake.</summary>
    public static void Install()
    {
        AppServices.IsDemo = true;

        // Start rendering generated images right away; each service's StartAsync awaits the ones it needs.
        DemoAssets.WarmUp();

        AppServices.Foreground = new FakeForegroundService();
        AppServices.Hotkeys = new FakeHotkeyService();
        AppServices.Wallpaper = new FakeWallpaperService();
        AppServices.Autostart = new FakeAutostartService();
        AppServices.Clipboard = new FakeClipboardService();

        AppServices.Media = new FakeMediaService();
        AppServices.Audio = new FakeAudioService();

        AppServices.Network = new FakeNetworkService();
        AppServices.Bluetooth = new FakeBluetoothService();
        AppServices.Display = new FakeDisplayService();

        AppServices.Stats = new FakeStatsService();
        AppServices.SpeedTest = new FakeSpeedTestService();
        AppServices.Power = new FakePowerService();

        AppServices.Weather = new FakeWeatherService();
        AppServices.Calendar = new FakeCalendarService();
        AppServices.Todos = new FakeTodoService();
        AppServices.Timer = new FakeTimerService();

        AppServices.Phone = new FakePhoneService();

        Log.Info($"Demo services installed (live events: {LiveEvents}, assets: {DemoAssets.Directory})");
    }
}
