using System.Windows;
using System.Windows.Threading;
using NotchHub.App.Shell;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.UI.Animation;
using NotchHub.UI.Localization;
using NotchHub.UI.Theming;

namespace NotchHub.App;

/// <summary>
/// Composition root. Command line:
///   --demo                fake services with sample data (no hardware / network access)
///   --data &lt;dir&gt;          override the data directory (default %APPDATA%\NotchHub)
///   --screenshot &lt;dir&gt;    render every state to PNGs (use with --demo) and exit
///   --state collapsed|peek|expanded, --tab &lt;name&gt;   initial state / tab
///   --verbose             debug logging
///   --cycle-tabs          diagnostics: after start, open every tab once, then collapse (memory measurements)
///   --idle-release &lt;s&gt;    diagnostics: release hidden tab views after s seconds instead of 2 minutes
/// </summary>
public partial class App : Application
{
    private CancellationTokenSource? _cts;
    private SingleInstance? _instance;
    private NotchWindow? _window;
    private MemoryTrimmer? _trimmer;
    private TrayIcon? _tray;
    private AppBarReservation? _appBar;
    private ThemeService? _theme;
    private bool _fgFullscreen, _abFullscreen;
    private string? _language, _hkToggle, _hkClipboard;
    private (bool, double, int)? _appBarState;
    private TimerChime? _chime;
    private string? _chargeDevice;
    private bool? _chargeLast;
    private string? _callKey;
    private bool? _autostartApplied;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await StartAsync(e.Args);
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            Shutdown(1);
        }
    }

    private async Task StartAsync(string[] args)
    {
        AppPaths.Initialize(ArgValue(args, "--data"));
        Log.Verbose = args.Contains("--verbose");
        AppServices.IsDemo = args.Contains("--demo");
        var screenshotDir = ArgValue(args, "--screenshot");
        var screenshot = screenshotDir is not null;

        if (!screenshot)
        {
            _instance = SingleInstance.TryAcquire(AppPaths.Root, () => AppServices.Shell?.Expand());
            if (_instance is null)
            {
                Log.Info("Another instance is running — asked it to open the panel");
                Shutdown(0);
                return;
            }
        }
        InstallCrashHandlers();

        AppServices.Ui = new UiDispatcher(Dispatcher);
        var settings = new SettingsService();
        AppServices.Settings = settings;

        foreach (var asm in FeatureAssemblies) Log.Safe($"Loc.Register {asm}", () => Loc.Register(asm));
        _language = settings.Current.Language;
        Loc.SetLanguage(_language);
        Motion.ReduceMotion = screenshot || settings.Current.Appearance.ReduceMotion;
        Motion.Profile = ProfileOf(settings.Current.Appearance.AnimationStyle);

        if (AppServices.IsDemo)
        {
            if (screenshot) Demo.DemoServices.LiveEvents = false;
            Demo.DemoServices.Install();
        }
        else Composition.InstallRealServices(settings);

        _cts = new CancellationTokenSource();
        _theme = new ThemeService(settings, AppServices.Wallpaper);

        _window = new NotchWindow(settings) { ScreenshotMode = screenshot };
        AppServices.Shell = _window;
        if (!screenshot)
        {
            TimeSpan? releaseAfter = int.TryParse(ArgValue(args, "--idle-release"), out var sec) && sec > 0 ? TimeSpan.FromSeconds(sec) : null;
            _trimmer = new MemoryTrimmer(_window, Dispatcher, releaseAfter);
        }
        MainWindow = _window;

        if (screenshot) await StartServicesAsync(); // views should see started services in the shots
        if (!screenshot) Log.Safe("Mods", () => NotchHub.UI.Modding.ModLoader.LoadAll(settings));
        _window.Show();
        Log.Safe("SetOwnWindow", () => AppServices.Foreground.SetOwnWindow(_window.Handle));
        WireEvents(settings);

        if (screenshot)
        {
            await ScreenshotRunner.RunAsync(_window, screenshotDir!);
            Shutdown(0);
            return;
        }

        _tray = new TrayIcon();
        _tray.OpenRequested += (_, _) => _window.Expand();
        _tray.SettingsRequested += (_, _) => _window.Expand(ShellTab.Settings);
        _tray.ExitRequested += (_, _) => Shutdown(0);

        _appBar = new AppBarReservation();
        _appBar.FullscreenChanged += (_, fs) => { _abFullscreen = fs; UpdateFullscreen(); };
        _window.IdleHeightChanged += (_, _) => ApplyAppBar(settings.Current);
        ApplySettings(settings.Current);

        ApplyInitialState(args);
        await StartServicesAsync();
        UpdateFullscreen();
        _window.UpdatePhoneChip();
        if (args.Contains("--cycle-tabs")) await CycleTabsAsync();
    }

    /// <summary>Diagnostics (--cycle-tabs): shows every tab once without activating the window, then collapses.</summary>
    private async Task CycleTabsAsync()
    {
        var w = _window!;
        await Task.Delay(1500);
        w.Expand(NotchWindow.StripTabs[0], activate: false);
        foreach (var tab in NotchWindow.StripTabs.Append(ShellTab.Settings))
        {
            w.SelectTab(tab);
            await Task.Delay(1500);
        }
        w.SelectTab(NotchWindow.StripTabs[0]);
        await Task.Delay(500);
        w.Collapse();
        Log.Info("cycle-tabs: done");
    }

    private async Task StartServicesAsync()
    {
        foreach (var service in AppServices.BackgroundServices())
        {
            try { await service.StartAsync(_cts!.Token); }
            catch (Exception ex) { Log.Error($"Start failed: {service.GetType().Name}", ex); }
        }
        try { await _theme!.StartAsync(_cts!.Token); }
        catch (Exception ex) { Log.Error("Start failed: ThemeService", ex); }
    }

    private void ApplyInitialState(string[] args)
    {
        ShellTab? tab = Enum.TryParse<ShellTab>(ArgValue(args, "--tab"), true, out var t) ? t : null;
        switch (ArgValue(args, "--state")?.ToLowerInvariant())
        {
            case "peek": _window!.SetState(ShellState.Peek); break;
            case "expanded": _window!.Expand(tab, activate: false); break;
            default: if (tab is not null) _window!.SelectTab(tab.Value); break;
        }
    }

    // ───────────────────────────── wiring ─────────────────────────────

    private void WireEvents(ISettingsService settings)
    {
        var shell = _window!;
        void Ui(Action a) => Dispatcher.BeginInvoke(a);

        Hook("Phone", () =>
        {
            var phone = AppServices.Phone;
            phone.Changed += (_, _) =>
            {
                shell.UpdatePhoneChip();
                Ui(() => Log.Safe("Phone status HUD", () => CheckPhoneStatus(settings.Current)));
            };
            phone.NotificationPosted += (_, e) =>
            {
                shell.UpdatePhoneChip();
                var s = settings.Current.Phone;
                var n = e.Notification;
                if (!s.NotificationPeeks || n.IsSilent) return;
                if (s.MutedApps.Any(a => string.Equals(a, n.AppName, StringComparison.OrdinalIgnoreCase))) return;
                shell.ShowHud(new HudRequest(HudKind.PhoneNotification, n));
            };
            phone.PairRequested += (_, device) => shell.ShowHud(new HudRequest(HudKind.PhonePairRequest, device, TimeSpan.FromSeconds(30)));
            phone.ClipboardReceived += (_, text) =>
            {
                if (!settings.Current.Phone.ReceiveClipboard || string.IsNullOrEmpty(text)) return;
                Log.Safe("Clipboard.AddExternal", () => AppServices.Clipboard.AddExternal(text, ClipOrigin.Phone, true));
            };
        });
        _chime = new TimerChime(Dispatcher);
        Hook("Timer", () => AppServices.Timer.Finished += (_, _) =>
        {
            shell.ShowHud(new HudRequest(HudKind.TimerFinished));
            // the melody plays twice while the HUD is up (once if the panel was open); dismissing the HUD stops it
            Ui(() => Log.Safe("Timer sound", () => _chime.Play(shell.CurrentHud == HudKind.TimerFinished ? 2 : 1)));
        });
        ((IShell)shell).StateChanged += (_, _) =>
        {
            if (_chime.IsPlaying && shell.CurrentHud != HudKind.TimerFinished) _chime.Stop();
        };
        Hook("Audio", () => AppServices.Audio.VolumeChanged += (_, e) =>
        {
            if (e.IsExternal && settings.Current.Notch.ShowHud)
                shell.ShowHud(new HudRequest(HudKind.Volume, new HudLevel(e.Volume, e.IsMuted)));
        });
        Hook("Foreground", () => AppServices.Foreground.FullscreenChanged += (_, _) =>
        {
            _fgFullscreen = AppServices.Foreground.IsFullscreenAppActive;
            UpdateFullscreen();
        });
        settings.Changed += (_, _) => Ui(() => ApplySettings(settings.Current));

        void Hook(string name, Action wire) => Log.Safe($"Wire {name}", wire);
    }

    private void UpdateFullscreen() => _window?.UpdateFullscreen(_fgFullscreen || _abFullscreen);

    /// <summary>Applies settings that live outside the notch window (language, motion, autostart, hotkeys, AppBar).</summary>
    private void ApplySettings(AppSettings s)
    {
        if (s.Language != _language)
        {
            _language = s.Language;
            Log.Safe("Loc.SetLanguage", () => Loc.SetLanguage(s.Language));
        }
        Motion.ReduceMotion = s.Appearance.ReduceMotion;
        Motion.Profile = ProfileOf(s.Appearance.AnimationStyle);

        Log.Safe("Autostart", () => ApplyAutostart(s));

        Log.Safe("Hotkeys", () =>
        {
            var hk = AppServices.Hotkeys;
            if (hk is null) return;
            if (s.Shortcuts.TogglePanel != _hkToggle)
            {
                _hkToggle = s.Shortcuts.TogglePanel;
                RegisterHotkey(hk, "toggle", _hkToggle, () => _window?.ToggleFromHotkey(null));
            }
            if (s.Shortcuts.OpenClipboard != _hkClipboard)
            {
                _hkClipboard = s.Shortcuts.OpenClipboard;
                RegisterHotkey(hk, "clipboard", _hkClipboard, () => _window?.ToggleFromHotkey(ShellTab.Clipboard));
            }
        });

        ApplyAppBar(s);
    }

    /// <summary>
    /// The Run key (and the Task Manager "Startup apps" toggle) is the source of truth: on the first pass the setting is
    /// reconciled from the registry (installer choice, Task Manager choice) without writing it; afterwards the registry
    /// is only written when the setting itself changes (the user flipped the toggle), never just because they differ.
    /// </summary>
    private void ApplyAutostart(AppSettings s)
    {
        var autostart = AppServices.Autostart;
        if (autostart is null) return;
        var wanted = s.General.StartWithWindows;
        if (_autostartApplied is null)
        {
            var live = autostart.IsEnabled;
            _autostartApplied = live;
            if (live != wanted) AppServices.Settings.Update(x => x.General.StartWithWindows = live);
            return;
        }
        if (wanted == _autostartApplied) return;
        _autostartApplied = wanted;
        if (autostart.IsEnabled == wanted) return;
        if (!autostart.SetEnabled(wanted)) Log.Warn("Autostart change failed");
    }

    private static MotionProfile ProfileOf(AnimationStyle style) => style switch
    {
        AnimationStyle.Legacy => MotionProfile.Legacy,
        AnimationStyle.Smooth => MotionProfile.Smooth,
        _ => MotionProfile.Refreshed,
    };

    /// <summary>AppBar strip: plain notch, live-activity height, or (Dynamic) whatever the idle notch shows right now.</summary>
    private void ApplyAppBar(AppSettings s) => Log.Safe("AppBar", () =>
    {
        var n = s.Notch;
        var height = n.ReservedSpace switch
        {
            ReservedSpaceMode.LiveActivity => NotchWindow.LiveHeight,
            ReservedSpaceMode.Dynamic => _window?.IdleHeight ?? n.CollapsedHeight,
            _ => n.CollapsedHeight,
        };
        var state = (n.ReserveScreenSpace, height, n.MonitorIndex);
        if (_appBar is null || _appBarState == state) return;
        // cache only on success: a failed ABM_NEW (taskbar not up yet at logon) is retried on the next call / TaskbarCreated
        _appBarState = _appBar.Update(state.ReserveScreenSpace, height, n.MonitorIndex) ? state : null;
    });

    /// <summary>Phone charging HUD (charging flips while connected) and call status HUD (ringing stays while it rings). UI thread.</summary>
    private void CheckPhoneStatus(AppSettings s)
    {
        var phone = AppServices.Phone;
        var shell = _window;
        if (phone is null || shell is null) return;

        var device = phone.ActiveDevice;
        var battery = phone.Battery;
        if (device is null || !device.IsReachable || battery is null)
        {
            _chargeDevice = null;
            _chargeLast = null;
        }
        else
        {
            if (_chargeDevice == device.Id && _chargeLast is { } last && last != battery.IsCharging && s.Phone.ChargingHud)
                shell.ShowHud(new HudRequest(HudKind.PhoneCharging, battery));
            _chargeDevice = device.Id;
            _chargeLast = battery.IsCharging;
        }

        var call = s.Phone.CallStatus ? phone.ActiveCall : null;
        var key = call is null ? null : $"{call.State}|{call.PhoneNumber}|{call.ContactName}";
        if (key == _callKey) return;
        var wasRinging = _callKey?.StartsWith(nameof(PhoneCallState.Ringing), StringComparison.Ordinal) == true;
        _callKey = key;
        switch (call?.State)
        {
            case PhoneCallState.Ringing:
            case PhoneCallState.Talking:
                shell.ShowHud(new HudRequest(HudKind.PhoneCall, call));
                break;
            case PhoneCallState.Missed when wasRinging:
                shell.ShowHud(new HudRequest(HudKind.PhoneCall, call, TimeSpan.FromSeconds(6)));
                break;
            default:
                shell.DismissHud(HudKind.PhoneCall);
                break;
        }
    }

    private static void RegisterHotkey(IHotkeyService hk, string id, string? gesture, Action action)
    {
        if (string.IsNullOrWhiteSpace(gesture)) { hk.Unregister(id); return; }
        if (!hk.Register(id, gesture, action)) Log.Warn($"Hotkey '{id}' ({gesture}) could not be registered");
    }

    // ───────────────────────────── shutdown / crash ─────────────────────────────

    private void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            if (e.IsTerminating) Cleanup();
        };
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Releases OS-level registrations (AppBar, tray icon). Idempotent, any thread.</summary>
    private void Cleanup()
    {
        try { _chime?.Stop(); } catch { /* best effort */ }
        try { _appBar?.Remove(); } catch { /* best effort during crash */ }
        try { _tray?.Remove(); } catch { /* best effort during crash */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Safe("Save last tab", () => _window?.FlushLastTab());
        Log.Safe("Mods shutdown", NotchHub.UI.Modding.ModLoader.ShutdownAll);
        Cleanup();
        _cts?.Cancel();
        Log.Safe("Dispose AppBar", () => _appBar?.Dispose());
        Log.Safe("Dispose tray", () => _tray?.Dispose());
        if (_window is not null)
        {
            foreach (var service in AppServices.BackgroundServices().Reverse())
                Log.Safe($"Dispose {service.GetType().Name}", service.Dispose);
        }
        Log.Safe("Dispose theme", () => _theme?.Dispose());
        _instance?.Dispose();
        base.OnExit(e);
    }

    public static readonly string[] FeatureAssemblies =
    [
        "NotchHub.Features.Controls", "NotchHub.Features.Today", "NotchHub.Features.Stats", "NotchHub.Features.Clipboard",
        "NotchHub.Features.Phone", "NotchHub.Features.Power", "NotchHub.Features.Settings", "NotchHub.Features.Compact",
        "NotchHub", // the App assembly (AssemblyName=NotchHub)
    ];

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
