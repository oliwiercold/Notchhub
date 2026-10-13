using NotchHub.Core;
using NotchHub.Core.Settings;

namespace NotchHub.App;

/// <summary>Creates the real (Windows) service implementations.</summary>
public static class Composition
{
    public static void InstallRealServices(ISettingsService settings)
    {
        AppServices.Foreground = new Platform.Shell.Foreground.ForegroundService();
        AppServices.Hotkeys = new Platform.Shell.Hotkeys.HotkeyService();
        AppServices.Wallpaper = new Platform.Shell.Wallpaper.WallpaperService();
        AppServices.Autostart = new Platform.Shell.Autostart.AutostartService();
        AppServices.Clipboard = new Platform.Shell.Clipboard.ClipboardService(settings);

        AppServices.Media = new Platform.Media.Media.MediaService();
        AppServices.Audio = new Platform.Media.Audio.AudioService();

        AppServices.Network = new Platform.Connectivity.Network.NetworkService();
        AppServices.Bluetooth = new Platform.Connectivity.Bluetooth.BluetoothService();
        AppServices.Display = new Platform.Connectivity.Display.DisplayService();

        AppServices.Stats = new Platform.Machine.Stats.StatsService(settings);
        AppServices.SpeedTest = new Platform.Machine.SpeedTest.SpeedTestService();
        AppServices.Power = new Platform.Machine.Power.PowerService();

        AppServices.Weather = new Services.Weather.WeatherService(settings);
        AppServices.Calendar = new Services.Calendar.CalendarService(settings);
        AppServices.Todos = new Services.Todos.TodoService();
        AppServices.Timer = new Services.Timer.TimerService();

        AppServices.Phone = new KdeConnect.PhoneService(settings);
    }
}
