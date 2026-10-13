using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.SampleData;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo network: connected to the Wi-Fi "Home 5G" (4 bars, internet). The Wi-Fi radio toggle, scanning (1.2 s),
/// connecting to saved networks and disconnecting work in memory — the real radio is never touched.
/// </summary>
public sealed class FakeNetworkService : FakeBackgroundService, INetworkService
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(1500);

    private NetworkStatus _status = Connected(WifiCatalog.Visible[0]);
    private int _radioGeneration; // invalidates a pending auto-reconnect when the radio is toggled again

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public NetworkStatus Status
    {
        get { lock (Gate) return _status; }
    }

    /// <inheritdoc />
    public async Task<bool> SetWifiEnabledAsync(bool enabled)
    {
        await Task.Delay(250).ConfigureAwait(false);
        int generation;
        lock (Gate)
        {
            if (_status.WifiEnabled == enabled) return true;
            generation = ++_radioGeneration;
            _status = enabled ? Disconnected(wifiEnabled: true) : Disconnected(wifiEnabled: false);
        }
        Log.Info($"DEMO: Wi-Fi radio {(enabled ? "on" : "off")}");
        Raise(Changed);

        if (enabled)
        {
            // Windows reconnects to the preferred network shortly after the radio comes back.
            After(ReconnectDelay, "Wi-Fi auto-reconnect", () =>
            {
                lock (Gate)
                {
                    if (generation != _radioGeneration || !_status.WifiEnabled || _status.Name is not null) return;
                    _status = Connected(WifiCatalog.Visible[0]);
                }
                Raise(Changed);
            });
        }
        return true;
    }

    /// <inheritdoc />
    public bool LastScanBlockedByLocation => false;

    public async Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken ct = default)
    {
        await Task.Delay(ScanDuration, ct).ConfigureAwait(false);
        NetworkStatus status;
        lock (Gate) status = _status;
        if (!status.WifiEnabled) return [];
        return WifiCatalog.Visible
            .Select(n => new WifiNetwork(n.Ssid, n.SignalPercent, n.IsSecured, n.Ssid == status.Name, n.HasProfile, n.Band))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> ConnectWifiAsync(string ssid)
    {
        var network = WifiCatalog.Visible.FirstOrDefault(n => n.Ssid == ssid);
        if (network is not { HasProfile: true }) return false;
        return await ConnectCoreAsync(network.Ssid, network).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Demo: any password of 8+ characters is accepted for secured networks (except "wrongpass", to exercise the error path).</remarks>
    public async Task<bool> ConnectWifiAsync(string ssid, string? password)
    {
        var network = WifiCatalog.Visible.FirstOrDefault(n => n.Ssid == ssid);
        if (network is null) return false;
        if (network.IsSecured && (password is null || password.Length < 8 || password == "wrongpass"))
        {
            await Task.Delay(1200).ConfigureAwait(false);
            return false;
        }
        return await ConnectCoreAsync(ssid, network).ConfigureAwait(false);
    }

    private async Task<bool> ConnectCoreAsync(string ssid, DemoWifi network)
    {
        await Task.Delay(800).ConfigureAwait(false);
        lock (Gate)
        {
            if (!_status.WifiEnabled) return false;
            if (_status.Name == ssid) return true;
            _radioGeneration++;
            _status = Connected(network);
        }
        Raise(Changed);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DisconnectWifiAsync()
    {
        await Task.Delay(200).ConfigureAwait(false);
        lock (Gate)
        {
            if (_status.Name is null) return true;
            _radioGeneration++;
            _status = Disconnected(_status.WifiEnabled);
        }
        Raise(Changed);
        return true;
    }

    /// <inheritdoc />
    public void OpenSettings() => Log.Info("DEMO: would open Windows Wi-Fi settings");

    private static NetworkStatus Connected(DemoWifi network) => new()
    {
        Kind = NetworkKind.WiFi,
        Name = network.Ssid,
        SignalBars = WifiCatalog.Bars(network.SignalPercent),
        HasInternet = true,
        IsMetered = false,
        LocalIPv4 = "192.168.1.42",
        WifiAdapterPresent = true,
        WifiEnabled = true,
        CanToggleWifi = true,
    };

    private static NetworkStatus Disconnected(bool wifiEnabled) => new()
    {
        Kind = NetworkKind.None,
        WifiAdapterPresent = true,
        WifiEnabled = wifiEnabled,
        CanToggleWifi = true,
    };
}
