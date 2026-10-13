using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Connectivity.Internal;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace NotchHub.Platform.Connectivity.Bluetooth;

/// <summary>
/// Bluetooth radio (Windows.Devices.Radios) and paired devices (classic + LE association endpoints, watched — no
/// polling). Battery levels come from the PnP battery property of the device container (best effort, connected
/// devices only). Connect is best effort; Windows has no public disconnect API, so that returns false.
/// </summary>
public sealed class BluetoothService : IBluetoothService
{
    private const string ClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LeProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string CodMajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string LeAppearanceKey = "System.Devices.Aep.Bluetooth.Le.Appearance.Category";
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private readonly object _gate = new();
    private readonly RadioMonitor _radio = new(RadioKind.Bluetooth);
    private readonly Dictionary<string, DeviceEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Debouncer _publish;
    private DeviceWatcher? _watcher;
    private IReadOnlyList<BluetoothDeviceInfo> _devices = [];
    private bool _started;
    private bool _disposed;

    private sealed class DeviceEntry
    {
        public required string Id { get; init; }
        public string Name { get; set; } = "";
        public bool IsConnected { get; set; }
        public Guid? ContainerId { get; set; }
        public BluetoothDeviceKind Kind { get; set; } = BluetoothDeviceKind.Other;
        public int? Battery { get; set; }
    }

    public BluetoothService()
    {
        _publish = new Debouncer(TimeSpan.FromMilliseconds(300), () => PublishAsync().Forget("Bluetooth.Publish"), "BluetoothService.Publish");
    }

    public bool IsAvailable => _radio.IsPresent;
    public bool IsEnabled => _radio.IsOn;
    public bool CanToggle => _radio.CanToggle;
    public IReadOnlyList<BluetoothDeviceInfo> Devices => Volatile.Read(ref _devices);

    public event EventHandler? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _radio.Changed += OnRadioChanged;
        try { await _radio.StartAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { Log.Warn("Bluetooth: radio unavailable", ex); }

        try
        {
            var aqs = $"(System.Devices.Aep.ProtocolId:=\"{ClassicProtocol}\" OR System.Devices.Aep.ProtocolId:=\"{LeProtocol}\")"
                      + " AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
            var watcher = DeviceInformation.CreateWatcher(aqs, [IsConnectedKey, ContainerKey, CodMajorKey, LeAppearanceKey],
                DeviceInformationKind.AssociationEndpoint);
            watcher.Added += (_, info) => Upsert(info);
            watcher.Updated += (_, update) => Update(update);
            watcher.Removed += (_, update) =>
            {
                lock (_gate) _entries.Remove(update.Id);
                _publish.Trigger();
            };
            watcher.EnumerationCompleted += (_, _) => _publish.Trigger();
            _watcher = watcher;
            watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Warn("Bluetooth: device watcher unavailable", ex);
        }
        Log.Info($"Bluetooth: available={IsAvailable}, on={IsEnabled}, canToggle={CanToggle}");
    }

    private void OnRadioChanged()
    {
        RaiseChanged();
        _publish.Trigger(); // connection states change with the radio
    }

    private void Upsert(DeviceInformation info)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(info.Id, out var entry))
            {
                entry = new DeviceEntry { Id = info.Id };
                _entries[info.Id] = entry;
            }
            entry.Name = string.IsNullOrWhiteSpace(info.Name) ? entry.Name : info.Name;
            Apply(entry, info.Properties);
        }
        _publish.Trigger();
    }

    private void Update(DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(update.Id, out var entry)) return;
            Apply(entry, update.Properties);
        }
        _publish.Trigger();
    }

    private static void Apply(DeviceEntry entry, IReadOnlyDictionary<string, object> props)
    {
        if (props.TryGetValue(IsConnectedKey, out var c) && c is bool connected) entry.IsConnected = connected;
        if (props.TryGetValue(ContainerKey, out var g) && g is Guid container) entry.ContainerId = container;
        if (props.TryGetValue(CodMajorKey, out var major) && major is not null)
        {
            entry.Kind = Convert.ToInt32(major) switch
            {
                1 => BluetoothDeviceKind.Computer,
                2 => BluetoothDeviceKind.Phone,
                4 => BluetoothDeviceKind.Audio,
                5 => BluetoothDeviceKind.Input,
                7 => BluetoothDeviceKind.Wearable,
                _ => entry.Kind,
            };
        }
        if (entry.Kind == BluetoothDeviceKind.Other && props.TryGetValue(LeAppearanceKey, out var cat) && cat is not null)
        {
            entry.Kind = Convert.ToInt32(cat) switch
            {
                0x01 => BluetoothDeviceKind.Phone,
                0x02 => BluetoothDeviceKind.Computer,
                0x03 or 0x05 or 0x0D => BluetoothDeviceKind.Wearable,
                0x0F or 0x0A => BluetoothDeviceKind.Input,
                0x29 or 0x21 or 0x25 => BluetoothDeviceKind.Audio,
                _ => entry.Kind,
            };
        }
    }

    private async Task PublishAsync()
    {
        if (_disposed) return;
        DeviceEntry[] snapshot;
        lock (_gate) snapshot = [.. _entries.Values];

        foreach (var entry in snapshot)
        {
            if (!entry.IsConnected || entry.ContainerId is not Guid container) { entry.Battery = null; continue; }
            entry.Battery = await ReadBatteryAsync(container).ConfigureAwait(false);
        }

        // Classic + LE endpoints of one physical device share a container: keep one row (prefer connected / classic).
        var list = snapshot
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.ContainerId?.ToString() ?? e.Id)
            .Select(g => g.OrderByDescending(e => e.IsConnected).ThenByDescending(e => e.Battery.HasValue).First())
            .Select(e => new BluetoothDeviceInfo(e.Id, e.Name, e.Kind, e.IsConnected, e.Battery))
            .OrderByDescending(d => d.IsConnected).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var previous = Volatile.Read(ref _devices);
        if (previous.SequenceEqual(list)) return;
        Volatile.Write(ref _devices, list);
        RaiseChanged();
    }

    private static async Task<int?> ReadBatteryAsync(Guid container)
    {
        try
        {
            var aqs = $"System.Devices.ContainerId:=\"{{{container}}}\"";
            var devices = await DeviceInformation.FindAllAsync(aqs, [BatteryKey], DeviceInformationKind.Device).AsTask().ConfigureAwait(false);
            foreach (var d in devices)
            {
                if (d.Properties.TryGetValue(BatteryKey, out var v) && v is not null)
                {
                    var percent = Convert.ToInt32(v);
                    if (percent is >= 0 and <= 100) return percent;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Bluetooth: battery query failed", ex);
        }
        return null;
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("Bluetooth: Changed handler failed", ex); }
    }

    public Task<bool> SetEnabledAsync(bool enabled) => _radio.SetStateAsync(enabled);

    public async Task<bool> ConnectAsync(string deviceId)
    {
        try
        {
            // Opening the device and querying its services un-cached makes Windows establish a link; audio devices
            // usually reconnect their profiles from there.
            using var classic = await BluetoothDevice.FromIdAsync(deviceId).AsTask().ConfigureAwait(false);
            if (classic is not null)
            {
                if (classic.ConnectionStatus == BluetoothConnectionStatus.Connected) return true;
                await classic.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask().ConfigureAwait(false);
                return await WaitConnectedAsync(deviceId).ConfigureAwait(false);
            }
            using var le = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask().ConfigureAwait(false);
            if (le is null) return false;
            if (le.ConnectionStatus == BluetoothConnectionStatus.Connected) return true;
            await le.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().ConfigureAwait(false);
            return le.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        catch (Exception ex)
        {
            Log.Warn("Bluetooth: connect failed", ex);
            return false;
        }
    }

    private async Task<bool> WaitConnectedAsync(string deviceId)
    {
        for (var i = 0; i < 20; i++)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(deviceId, out var e) && e.IsConnected) return true;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>No public API disconnects a paired device; the UI falls back to Windows settings.</summary>
    public Task<bool> DisconnectAsync(string deviceId) => Task.FromResult(false);

    public void OpenSettings() => SettingsLauncher.Open("ms-settings:bluetooth");

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _radio.Changed -= OnRadioChanged;
        try
        {
            if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted }) _watcher.Stop();
        }
        catch (Exception ex) { Log.Warn("Bluetooth: stopping the watcher failed", ex); }
        _publish.Dispose();
        _radio.Dispose();
    }
}
