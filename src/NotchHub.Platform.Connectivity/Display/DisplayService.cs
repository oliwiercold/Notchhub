using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Connectivity.Display.NightLight;
using NotchHub.Platform.Connectivity.Internal;

namespace NotchHub.Platform.Connectivity.Display;

/// <summary>
/// Night light (CloudStore blob, registry-change driven), Windows dark mode (Personalize key + WM_SETTINGCHANGE
/// broadcast) and brightness (WMI for internal panels with change events, DDC/CI for the primary external monitor).
/// Brightness writes are coalesced on a worker so slider drags never queue up slow DDC/CI calls.
/// </summary>
public sealed partial class DisplayService : IDisplayService
{
    private const string CloudStoreCurrent = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly object _gate = new();
    private readonly List<RegistryKeyWatcher> _watchers = [];
    private Debouncer? _refresh;
    private ManagementEventWatcher? _wmiWatcher;
    private string[] _nightLightKeys = [];
    private bool _nightLightSupported, _nightLightEnabled, _darkMode;
    private BrightnessBackend _backend = BrightnessBackend.None;
    private int _brightness;
    private int? _pendingBrightness;
    private bool _brightnessWorkerRunning;
    private bool _disposed;

    private enum BrightnessBackend { None, Wmi, Ddc }

    public bool NightLightSupported => _nightLightSupported;
    public bool NightLightEnabled => _nightLightEnabled;
    public bool DarkModeEnabled => _darkMode;
    public bool BrightnessSupported => _backend != BrightnessBackend.None;
    public int Brightness => Volatile.Read(ref _brightness);

    public event EventHandler? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _refresh = new Debouncer(TimeSpan.FromMilliseconds(250), () => { if (ReadRegistryState()) RaiseChanged(); }, "DisplayService.Refresh");
        await Task.Run(() =>
        {
            _nightLightKeys = FindNightLightKeys();
            ReadRegistryState();
            foreach (var key in _nightLightKeys.Append(PersonalizeKey))
            {
                var watcher = new RegistryKeyWatcher(key, _refresh.Trigger);
                watcher.Start();
                lock (_gate) _watchers.Add(watcher);
            }
            InitBrightness();
        }, cancellationToken).ConfigureAwait(false);
        Log.Info($"Display: nightLight={(_nightLightSupported ? _nightLightEnabled.ToString() : "n/a")}, dark={_darkMode}, brightness={_backend}:{_brightness}");
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("Display: Changed handler failed", ex); }
    }

    // ───────────────────────────── night light + dark mode ─────────────────────────────

    /// <summary>State keys: per-device (24H2+) first, then the classic one.</summary>
    private static string[] FindNightLightKeys()
    {
        var result = new List<string>();
        try
        {
            using var current = Registry.CurrentUser.OpenSubKey(CloudStoreCurrent);
            if (current is null) return [];
            foreach (var name in current.GetSubKeyNames())
            {
                var suffix = name.Contains('$') ? name[(name.IndexOf('$') + 1)..] : name;
                if (!suffix.Equals("windows.data.bluelightreduction.bluelightreductionstate", StringComparison.OrdinalIgnoreCase)
                    && !suffix.Equals("windows.data.bluelightreduction.bluelightreductionstateperdevice", StringComparison.OrdinalIgnoreCase)) continue;
                var path = $@"{CloudStoreCurrent}\{name}\{suffix}";
                using var key = Registry.CurrentUser.OpenSubKey(path);
                if (NightLightBlob.TryParse(key?.GetValue("Data") as byte[]) is null) continue;
                if (suffix.EndsWith("perdevice", StringComparison.OrdinalIgnoreCase)) result.Insert(0, path);
                else result.Add(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Display: night light keys unreadable", ex);
        }
        return [.. result];
    }

    private static NightLightBlob? ReadBlob(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return NightLightBlob.TryParse(key?.GetValue("Data") as byte[]);
    }

    /// <summary>Re-reads night light + dark mode. Returns true when something changed.</summary>
    private bool ReadRegistryState()
    {
        if (_disposed) return false;
        bool supported = false, nightLight = false, dark = _darkMode;
        try
        {
            foreach (var path in _nightLightKeys)
            {
                var blob = ReadBlob(path);
                if (blob is null) continue;
                supported = true;
                nightLight = blob.IsActive;
                break;
            }
            using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            dark = personalize?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Display: reading the registry state failed", ex);
        }
        lock (_gate)
        {
            var changed = supported != _nightLightSupported || nightLight != _nightLightEnabled || dark != _darkMode;
            _nightLightSupported = supported;
            _nightLightEnabled = nightLight;
            _darkMode = dark;
            return changed;
        }
    }

    public Task<bool> SetNightLightAsync(bool enabled) => Task.Run(() =>
    {
        var any = false;
        foreach (var path in _nightLightKeys)
        {
            try
            {
                var blob = ReadBlob(path);
                if (blob is null) continue;
                if (blob.IsActive == enabled) { any = true; continue; }
                var data = blob.BuildToggled(enabled, DateTimeOffset.Now);
                using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
                if (key is null) continue;
                key.SetValue("Data", data, RegistryValueKind.Binary);
                any = true;
            }
            catch (Exception ex)
            {
                Log.Warn("Display: writing night light failed", ex);
            }
        }
        _refresh?.Trigger();
        return any;
    });

    public Task<bool> SetDarkModeAsync(bool enabled) => Task.Run(() =>
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true))
            {
                key.SetValue("AppsUseLightTheme", enabled ? 0 : 1, RegistryValueKind.DWord);
                key.SetValue("SystemUsesLightTheme", enabled ? 0 : 1, RegistryValueKind.DWord);
            }
            // Explorer and apps re-read the theme on this broadcast.
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 1000, out _);
            _refresh?.Trigger();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Display: switching dark mode failed", ex);
            return false;
        }
    });

    public void OpenNightLightSettings() => SettingsLauncher.Open("ms-settings:nightlight");

    public void OpenDisplaySettings() => SettingsLauncher.Open("ms-settings:display");

    // ───────────────────────────── brightness ─────────────────────────────

    private void InitBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness, Active FROM WmiMonitorBrightness");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    if (mo["Active"] is false) continue;
                    _brightness = Convert.ToInt32(mo["CurrentBrightness"]);
                    _backend = BrightnessBackend.Wmi;
                    break;
                }
            }
        }
        catch (ManagementException)
        {
            // No internal panel (desktop PC): WMI class not supported.
        }
        catch (Exception ex)
        {
            Log.Warn("Display: WMI brightness query failed", ex);
        }

        if (_backend == BrightnessBackend.Wmi)
        {
            try
            {
                _wmiWatcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                _wmiWatcher.EventArrived += (_, e) =>
                {
                    try
                    {
                        var value = Convert.ToInt32(e.NewEvent["Brightness"]);
                        if (Interlocked.Exchange(ref _brightness, value) != value) RaiseChanged();
                    }
                    catch (Exception ex) { Log.Warn("Display: brightness event unreadable", ex); }
                };
                _wmiWatcher.Start();
            }
            catch (Exception ex)
            {
                Log.Warn("Display: brightness events unavailable", ex);
            }
            return;
        }

        if (TryDdc(null, out var percent))
        {
            _backend = BrightnessBackend.Ddc;
            _brightness = percent;
        }
    }

    public Task<bool> SetBrightnessAsync(int percent)
    {
        if (_backend == BrightnessBackend.None) return Task.FromResult(false);
        percent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            _pendingBrightness = percent;
            if (_brightnessWorkerRunning) return Task.FromResult(true);
            _brightnessWorkerRunning = true;
        }
        return Task.Run(BrightnessWorker);
    }

    /// <summary>Applies the latest requested value until no newer request is pending.</summary>
    private bool BrightnessWorker()
    {
        var ok = true;
        while (true)
        {
            int value;
            lock (_gate)
            {
                if (_pendingBrightness is not int pending || _disposed)
                {
                    _brightnessWorkerRunning = false;
                    return ok;
                }
                value = pending;
                _pendingBrightness = null;
            }
            ok = _backend == BrightnessBackend.Wmi ? SetWmi(value) : TryDdc(value, out _);
            if (ok && Interlocked.Exchange(ref _brightness, value) != value) RaiseChanged();
        }
    }

    private static bool SetWmi(int percent)
    {
        try
        {
            using var cls = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
            foreach (ManagementObject mo in cls.GetInstances())
            {
                using (mo) mo.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)percent]);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Display: WMI set brightness failed", ex);
            return false;
        }
    }

    /// <summary>Reads (<paramref name="set"/> null) or writes the primary monitor's DDC/CI brightness.</summary>
    private static bool TryDdc(int? set, out int percent)
    {
        percent = 0;
        try
        {
            var monitor = MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);
            if (monitor == 0 || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) return false;
            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)) return false;
            try
            {
                foreach (var pm in monitors)
                {
                    if (!GetMonitorBrightness(pm.hPhysicalMonitor, out var min, out var cur, out var max) || max <= min) continue;
                    if (set is int target)
                    {
                        var raw = min + (uint)Math.Round((max - min) * target / 100.0);
                        if (!SetMonitorBrightness(pm.hPhysicalMonitor, raw)) continue;
                        percent = target;
                    }
                    else
                    {
                        percent = (int)Math.Round((cur - min) * 100.0 / (max - min));
                    }
                    return true;
                }
                return false;
            }
            finally
            {
                DestroyPhysicalMonitors(count, monitors);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Display: DDC/CI brightness failed", ex);
            return false;
        }
    }

    public void Dispose()
    {
        RegistryKeyWatcher[] watchers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            watchers = [.. _watchers];
            _watchers.Clear();
        }
        foreach (var w in watchers) w.Dispose();
        _refresh?.Dispose();
        try { _wmiWatcher?.Stop(); _wmiWatcher?.Dispose(); } catch { }
    }

    // ───────────────────────────── interop ─────────────────────────────

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly nint HWND_BROADCAST = 0xFFFF;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public nint hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint hwnd, int msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorBrightness(nint monitor, out uint min, out uint current, out uint max);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMonitorBrightness(nint monitor, uint brightness);
}
