namespace NotchHub.Core.Contracts;

// ───────────────────────────── Network ─────────────────────────────

public enum NetworkKind { None, Ethernet, WiFi, Cellular, Other }

public sealed record NetworkStatus
{
    public NetworkKind Kind { get; init; }
    /// <summary>SSID / connection profile name; null when disconnected.</summary>
    public string? Name { get; init; }
    /// <summary>0..4 for Wi-Fi / cellular; 4 for ethernet; 0 when disconnected.</summary>
    public int SignalBars { get; init; }
    public bool HasInternet { get; init; }
    public bool IsMetered { get; init; }
    public string? LocalIPv4 { get; init; }
    public bool WifiAdapterPresent { get; init; }
    /// <summary>Wi-Fi radio is on.</summary>
    public bool WifiEnabled { get; init; }
    /// <summary>Wi-Fi radio can be toggled from this app.</summary>
    public bool CanToggleWifi { get; init; }
}

public sealed record WifiNetwork(string Ssid, int SignalPercent, bool IsSecured, bool IsConnected, bool HasProfile, string? Band);

public interface INetworkService : IBackgroundService
{
    NetworkStatus Status { get; }
    event EventHandler? Changed;
    Task<bool> SetWifiEnabledAsync(bool enabled);
    /// <summary>Visible Wi-Fi networks, strongest first, de-duplicated by SSID.</summary>
    Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken ct = default);
    /// <summary>True when the last scan was refused because Windows location services are off (needed for WLAN scans on Windows 11);
    /// the UI then offers ms-settings:privacy-location.</summary>
    bool LastScanBlockedByLocation { get; }
    /// <summary>Connects to a network that has a saved profile. Returns false otherwise (UI then opens Windows settings).</summary>
    Task<bool> ConnectWifiAsync(string ssid);
    /// <summary>Connects to a network without a saved profile: creates a WPA2/WPA3-Personal (or open, when password is null) profile
    /// with the password and connects. Returns false on failure (wrong password, unsupported security).</summary>
    Task<bool> ConnectWifiAsync(string ssid, string? password);
    Task<bool> DisconnectWifiAsync();
    void OpenSettings();
}

// ───────────────────────────── Bluetooth ─────────────────────────────

public enum BluetoothDeviceKind { Audio, Input, Phone, Computer, Wearable, Other }

public sealed record BluetoothDeviceInfo(string Id, string Name, BluetoothDeviceKind Kind, bool IsConnected, int? BatteryPercent);

public interface IBluetoothService : IBackgroundService
{
    bool IsAvailable { get; }
    bool IsEnabled { get; }
    bool CanToggle { get; }
    /// <summary>Paired devices (connected first).</summary>
    IReadOnlyList<BluetoothDeviceInfo> Devices { get; }
    event EventHandler? Changed;
    Task<bool> SetEnabledAsync(bool enabled);
    /// <summary>Best effort connect (works for most audio devices). Returns false when not possible.</summary>
    Task<bool> ConnectAsync(string deviceId);
    Task<bool> DisconnectAsync(string deviceId);
    void OpenSettings();
}

// ───────────────────────────── Display ─────────────────────────────

public interface IDisplayService : IBackgroundService
{
    bool NightLightSupported { get; }
    bool NightLightEnabled { get; }
    /// <summary>Windows dark mode (apps + system).</summary>
    bool DarkModeEnabled { get; }
    bool BrightnessSupported { get; }
    /// <summary>0..100 (internal panel via WMI or external monitor via DDC/CI).</summary>
    int Brightness { get; }
    event EventHandler? Changed;
    Task<bool> SetNightLightAsync(bool enabled);
    Task<bool> SetDarkModeAsync(bool enabled);
    Task<bool> SetBrightnessAsync(int percent);
    void OpenNightLightSettings();
    void OpenDisplaySettings();
}
