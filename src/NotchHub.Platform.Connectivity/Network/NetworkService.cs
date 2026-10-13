using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Connectivity.Internal;
using NotchHub.Platform.Connectivity.Network.Wlan;
using Windows.Devices.Radios;
using Windows.Networking;
using Windows.Networking.Connectivity;

namespace NotchHub.Platform.Connectivity.Network;

/// <summary>
/// Connection status from <c>NetworkInformation</c> (event driven), Wi-Fi radio via <c>Windows.Devices.Radios</c>, and
/// scanning / connecting through the native WLAN API.
/// </summary>
public sealed class NetworkService : INetworkService
{
    private readonly object _gate = new();
    private readonly RadioMonitor _radio = new(RadioKind.WiFi);
    private readonly Debouncer _refresh;
    private WlanClient? _wlan;
    private NetworkStatus _status = new();
    private bool _started;
    private bool _disposed;

    public NetworkService()
    {
        _refresh = new Debouncer(TimeSpan.FromMilliseconds(300), Refresh, "NetworkService.Refresh");
    }

    public NetworkStatus Status => Volatile.Read(ref _status);

    /// <inheritdoc />
    public bool LastScanBlockedByLocation { get; private set; }

    public event EventHandler? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        await Task.Run(() =>
        {
            _wlan = WlanClient.TryOpen();
            if (_wlan is not null) _wlan.Notification += OnWlanNotification;
        }, cancellationToken).ConfigureAwait(false);

        _radio.Changed += _refresh.Trigger;
        try { await _radio.StartAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { Log.Warn("Network: Wi-Fi radio unavailable", ex); }

        try { NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged; }
        catch (Exception ex) { Log.Warn("Network: NetworkStatusChanged unavailable", ex); }

        await Task.Run(Refresh, cancellationToken).ConfigureAwait(false);
        var s = Status;
        Log.Info($"Network: {s.Kind}, internet={s.HasInternet}, wifiAdapter={s.WifiAdapterPresent}, wifiOn={s.WifiEnabled}");
    }

    private void OnNetworkStatusChanged(object sender) => _refresh.Trigger();

    private void OnWlanNotification(WlanEvent e)
    {
        if (e.Source == WlanNative.NotificationSourceMsm && e.Code is WlanNative.MsmSignalQualityChange or WlanNative.MsmConnected
                or WlanNative.MsmDisconnected or WlanNative.MsmRadioStateChange
            || e.Source == WlanNative.NotificationSourceAcm && e.Code is WlanNative.AcmConnectionComplete or WlanNative.AcmDisconnected
                or WlanNative.AcmInterfaceArrival or WlanNative.AcmInterfaceRemoval)
        {
            _refresh.Trigger();
        }
    }

    private void Refresh()
    {
        if (_disposed) return;
        NetworkStatus next;
        try { next = Compute(); }
        catch (Exception ex)
        {
            Log.Warn("Network: status query failed", ex);
            return;
        }
        var prev = Interlocked.Exchange(ref _status, next);
        if (prev == next) return;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("Network: Changed handler failed", ex); }
    }

    private NetworkStatus Compute()
    {
        var wlanInterfaces = _wlan?.GetInterfaces() ?? [];
        var wifiPresent = _radio.IsPresent || wlanInterfaces.Count > 0;
        bool wifiOn;
        if (_radio.IsPresent) wifiOn = _radio.IsOn;
        else wifiOn = wlanInterfaces.Any(i => _wlan?.QueryRadioOn(i.Id) == true);

        var status = new NetworkStatus
        {
            Kind = NetworkKind.None,
            WifiAdapterPresent = wifiPresent,
            WifiEnabled = wifiOn,
            CanToggleWifi = _radio.CanToggle,
        };

        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile is null) return status;
        var level = profile.GetNetworkConnectivityLevel();
        if (level == NetworkConnectivityLevel.None) return status;

        var adapter = profile.NetworkAdapter;
        var kind = profile.IsWlanConnectionProfile ? NetworkKind.WiFi
            : profile.IsWwanConnectionProfile ? NetworkKind.Cellular
            : adapter?.IanaInterfaceType switch
            {
                6 => NetworkKind.Ethernet,
                71 => NetworkKind.WiFi,
                243 or 244 => NetworkKind.Cellular,
                _ => NetworkKind.Other,
            };

        string? name = null;
        if (kind == NetworkKind.WiFi)
        {
            // GetConnectedSsid needs location permission on recent builds; the profile name is normally the SSID.
            try { name = profile.WlanConnectionProfileDetails?.GetConnectedSsid(); } catch { }
        }
        if (string.IsNullOrEmpty(name)) name = profile.ProfileName;

        var bars = 0;
        if (kind is NetworkKind.WiFi or NetworkKind.Cellular)
        {
            try { bars = Math.Clamp((int)(profile.GetSignalBars() ?? 0), 0, 5); } catch { }
            bars = bars >= 5 ? 4 : bars; // Windows reports 0..5; the contract uses 0..4
        }
        else
        {
            bars = 4;
        }

        var metered = false;
        try
        {
            var cost = profile.GetConnectionCost();
            metered = cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable || cost.Roaming || cost.OverDataLimit;
        }
        catch { }

        string? ipv4 = null;
        try
        {
            var adapterId = adapter?.NetworkAdapterId;
            foreach (var host in NetworkInformation.GetHostNames())
            {
                if (host.Type != HostNameType.Ipv4 || host.IPInformation?.NetworkAdapter is not { } a) continue;
                if (adapterId is null || a.NetworkAdapterId == adapterId)
                {
                    ipv4 = host.CanonicalName;
                    break;
                }
            }
        }
        catch { }

        return status with
        {
            Kind = kind,
            Name = name,
            SignalBars = bars,
            HasInternet = level == NetworkConnectivityLevel.InternetAccess,
            IsMetered = metered,
            LocalIPv4 = ipv4,
        };
    }

    public async Task<bool> SetWifiEnabledAsync(bool enabled)
    {
        var ok = await _radio.SetStateAsync(enabled).ConfigureAwait(false);
        _refresh.Trigger();
        return ok;
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanWifiAsync(CancellationToken ct = default)
    {
        var wlan = _wlan;
        if (wlan is null) return [];
        var interfaces = wlan.GetInterfaces();
        if (interfaces.Count == 0) return [];

        var waits = new List<Task>();
        foreach (var iface in interfaces)
        {
            var wait = WaitForAsync(e => e.InterfaceId == iface.Id && e.Source == WlanNative.NotificationSourceAcm
                                         && e.Code is WlanNative.AcmScanComplete or WlanNative.AcmScanFail, TimeSpan.FromSeconds(4), ct);
            if (wlan.StartScan(iface.Id) == WlanNative.ErrorSuccess) waits.Add(wait);
        }
        if (waits.Count > 0) await Task.WhenAll(waits).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var bySsid = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);
        LastScanBlockedByLocation = false;
        foreach (var iface in interfaces)
        {
            var rc = wlan.GetAvailableNetworks(iface.Id, out var entries);
            if (rc != WlanNative.ErrorSuccess)
            {
                if (rc == WlanNative.ErrorAccessDenied) LastScanBlockedByLocation = true;
                Log.Warn(rc == WlanNative.ErrorAccessDenied
                    ? "Network: Wi-Fi list denied - Windows location services are off (required for WLAN scans)"
                    : $"Network: WlanGetAvailableNetworkList failed ({rc})");
                continue;
            }
            var frequencies = wlan.GetStrongestBssFrequencies(iface.Id);
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.Ssid)) continue; // hidden network
                frequencies.TryGetValue(e.Ssid, out var khz);
                var band = khz == 0 ? null : khz < 3_000_000 ? "2.4 GHz" : khz < 5_925_000 ? "5 GHz" : "6 GHz";
                var candidate = new WifiNetwork(e.Ssid, Math.Clamp(e.SignalQuality, 0, 100), e.IsSecured, e.IsConnected, e.HasProfile, band);
                if (bySsid.TryGetValue(e.Ssid, out var existing))
                {
                    candidate = candidate with
                    {
                        SignalPercent = Math.Max(existing.SignalPercent, candidate.SignalPercent),
                        IsConnected = existing.IsConnected || candidate.IsConnected,
                        HasProfile = existing.HasProfile || candidate.HasProfile,
                        IsSecured = existing.IsSecured || candidate.IsSecured,
                        Band = existing.Band ?? candidate.Band,
                    };
                }
                bySsid[e.Ssid] = candidate;
            }
        }
        return bySsid.Values.OrderByDescending(n => n.IsConnected).ThenByDescending(n => n.SignalPercent).ToArray();
    }

    /// <summary>Connects using the saved profile for <paramref name="ssid"/> (false when none is saved). Runs off the UI thread.</summary>
    public Task<bool> ConnectWifiAsync(string ssid) => Task.Run(() => ConnectSavedAsync(ssid));

    private async Task<bool> ConnectSavedAsync(string ssid)
    {
        var wlan = _wlan;
        if (wlan is null || string.IsNullOrEmpty(ssid)) return false;
        foreach (var iface in wlan.GetInterfaces())
        {
            if (wlan.GetAvailableNetworks(iface.Id, out var entries) != WlanNative.ErrorSuccess) continue;
            var entry = entries.FirstOrDefault(e => e.HasProfile && e.Ssid == ssid && e.ProfileName.Length > 0);
            if (entry is null) continue;
            return await ConnectProfileAsync(wlan, iface.Id, entry.ProfileName, entry).ConfigureAwait(false);
        }
        Log.Info("Network: no saved profile for the selected network");
        return false;
    }

    /// <summary>
    /// Connects to a visible network with a new per-user profile built from its security (WPA3-SAE / WPA2-PSK / WPA-PSK /
    /// WEP / OWE / open). The profile is removed again when the attempt fails (wrong password, timeout).
    /// </summary>
    public Task<bool> ConnectWifiAsync(string ssid, string? password) => Task.Run(() => ConnectNewAsync(ssid, password));

    private async Task<bool> ConnectNewAsync(string ssid, string? password)
    {
        var wlan = _wlan;
        if (wlan is null || string.IsNullOrEmpty(ssid)) return false;
        foreach (var iface in wlan.GetInterfaces())
        {
            if (wlan.GetAvailableNetworks(iface.Id, out var entries) != WlanNative.ErrorSuccess) continue;
            var matches = entries.Where(e => e.Ssid == ssid && e.BssType != Dot11BssType.Independent).ToList();
            if (matches.Count == 0) continue;
            // Prefer the row without a profile: it carries the network's own security, not a saved profile's.
            var entry = matches.FirstOrDefault(e => !e.HasProfile) ?? matches[0];

            if (string.IsNullOrEmpty(password) && WlanProfileXml.NeedsKey(entry.AuthAlgorithm, entry.CipherAlgorithm))
            {
                // No key typed: fall back to a saved profile if there is one.
                var saved = matches.FirstOrDefault(e => e.HasProfile && e.ProfileName.Length > 0);
                return saved is not null && await ConnectProfileAsync(wlan, iface.Id, saved.ProfileName, saved).ConfigureAwait(false);
            }

            var xml = WlanProfileXml.Build(ssid, entry.SsidBytes, entry.AuthAlgorithm, entry.CipherAlgorithm, password);
            if (xml is null)
            {
                Log.Warn($"Network: unsupported Wi-Fi security (auth {entry.AuthAlgorithm}, cipher {entry.CipherAlgorithm}) or invalid key length");
                return false;
            }
            var rc = wlan.SetProfile(iface.Id, xml, out var reason);
            if (rc != WlanNative.ErrorSuccess)
            {
                Log.Warn($"Network: WlanSetProfile failed ({rc}, reason {reason})");
                return false;
            }
            var ok = await ConnectProfileAsync(wlan, iface.Id, ssid, entry).ConfigureAwait(false);
            if (!ok)
            {
                var del = wlan.DeleteProfile(iface.Id, ssid);
                if (del != WlanNative.ErrorSuccess) Log.Warn($"Network: WlanDeleteProfile failed ({del})");
            }
            return ok;
        }
        return false;
    }

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    /// <summary>WlanConnect (profile mode), then waits for the matching connection_complete / attempt_fail (at most 15 s).</summary>
    private async Task<bool> ConnectProfileAsync(WlanClient wlan, Guid ifaceId, string profileName, WlanNetworkEntry entry)
    {
        WlanEvent? result = null;
        var wait = WaitForAsync(e =>
        {
            if (e.InterfaceId != ifaceId || e.Source != WlanNative.NotificationSourceAcm
                || e.Code is not (WlanNative.AcmConnectionComplete or WlanNative.AcmConnectionAttemptFail)) return false;
            if (e.Ssid is { Length: > 0 } s && s != entry.Ssid) return false; // some other (auto) connection
            result = e;
            return true;
        }, ConnectTimeout, CancellationToken.None);
        var rc = wlan.Connect(ifaceId, profileName, entry.SsidBytes, entry.BssType);
        if (rc != WlanNative.ErrorSuccess)
        {
            Log.Warn($"Network: WlanConnect failed ({rc})");
            return false;
        }
        await wait.ConfigureAwait(false);
        _refresh.Trigger();
        var ok = result is { Code: WlanNative.AcmConnectionComplete, ReasonCode: null or 0 };
        Log.Info(result is null ? "Network: Wi-Fi connect timed out"
            : ok ? "Network: Wi-Fi connected" : $"Network: Wi-Fi connect failed (reason {result.Value.ReasonCode})");
        return ok;
    }

    public Task<bool> DisconnectWifiAsync()
    {
        var wlan = _wlan;
        if (wlan is null) return Task.FromResult(false);
        return Task.Run(() =>
        {
            var any = false;
            foreach (var iface in wlan.GetInterfaces())
            {
                if (iface.State != WlanInterfaceState.Connected) continue;
                any |= wlan.Disconnect(iface.Id) == WlanNative.ErrorSuccess;
            }
            _refresh.Trigger();
            return any;
        });
    }

    public void OpenSettings() => SettingsLauncher.Open("ms-settings:network-wifi");

    /// <summary>Completes when a WLAN notification matches or the timeout elapses (never faults).</summary>
    private Task WaitForAsync(Func<WlanEvent, bool> predicate, TimeSpan timeout, CancellationToken ct)
    {
        var wlan = _wlan;
        if (wlan is null) return Task.CompletedTask;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(WlanEvent e)
        {
            if (predicate(e)) tcs.TrySetResult();
        }
        wlan.Notification += Handler;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var reg = cts.Token.Register(() => tcs.TrySetResult());
        return tcs.Task.ContinueWith(_ =>
        {
            wlan.Notification -= Handler;
            reg.Dispose();
            cts.Dispose();
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged; } catch { }
        _radio.Changed -= _refresh.Trigger;
        _refresh.Dispose();
        _radio.Dispose();
        if (_wlan is not null)
        {
            _wlan.Notification -= OnWlanNotification;
            _wlan.Dispose();
        }
    }
}
