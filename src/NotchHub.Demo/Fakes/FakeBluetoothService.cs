using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo Bluetooth: adapter present but OFF (as in the reference screenshot) with three paired devices. Turning it on
/// auto-connects the headphones and the mouse after a moment; connect / disconnect work in memory.
/// </summary>
public sealed class FakeBluetoothService : FakeBackgroundService, IBluetoothService
{
    private const string HeadphonesId = @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}_LOCALMFG&0000\7&2b3c9f1&0&14A3C0D9E2F1_C00000000";
    private const string MouseId = @"BTHLE\Dev_e4c1d2a8b7f3\8&1f2a3b4c&0&e4c1d2a8b7f3";
    private const string PhoneId = @"BTHENUM\Dev_3C2EF91A7B44\7&1a2b3c4d&0&BluetoothDevice_3C2EF91A7B44";
    private static readonly TimeSpan AutoConnectDelay = TimeSpan.FromMilliseconds(1600);

    private bool _enabled;
    private int _radioGeneration;
    private BluetoothDeviceInfo[] _devices =
    [
        new(HeadphonesId, "WH-1000XM4", BluetoothDeviceKind.Audio, false, 80),
        new(MouseId, "MX Master 3S", BluetoothDeviceKind.Input, false, 55),
        new(PhoneId, "Pixel 8 Pro", BluetoothDeviceKind.Phone, false, null),
    ];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool IsEnabled
    {
        get { lock (Gate) return _enabled; }
    }

    /// <inheritdoc />
    public bool CanToggle => true;

    /// <inheritdoc />
    public IReadOnlyList<BluetoothDeviceInfo> Devices
    {
        get { lock (Gate) return _devices; }
    }

    /// <inheritdoc />
    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        await Task.Delay(300).ConfigureAwait(false);
        int generation;
        lock (Gate)
        {
            if (_enabled == enabled) return true;
            _enabled = enabled;
            generation = ++_radioGeneration;
            if (!enabled) SetConnected(_ => false);
        }
        Log.Info($"DEMO: Bluetooth radio {(enabled ? "on" : "off")}");
        Raise(Changed);

        if (enabled)
        {
            After(AutoConnectDelay, "Bluetooth auto-connect", () =>
            {
                lock (Gate)
                {
                    if (generation != _radioGeneration || !_enabled) return;
                    SetConnected(d => d.IsConnected || d.Id is HeadphonesId or MouseId);
                }
                Raise(Changed);
            });
        }
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(string deviceId)
    {
        await Task.Delay(900).ConfigureAwait(false);
        lock (Gate)
        {
            if (!_enabled || _devices.All(d => d.Id != deviceId)) return false;
            if (_devices.Any(d => d.Id == deviceId && d.IsConnected)) return true;
            SetConnected(d => d.IsConnected || d.Id == deviceId);
        }
        Raise(Changed);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DisconnectAsync(string deviceId)
    {
        await Task.Delay(400).ConfigureAwait(false);
        lock (Gate)
        {
            if (_devices.All(d => d.Id != deviceId)) return false;
            if (_devices.Any(d => d.Id == deviceId && !d.IsConnected)) return true;
            SetConnected(d => d.IsConnected && d.Id != deviceId);
        }
        Raise(Changed);
        return true;
    }

    /// <inheritdoc />
    public void OpenSettings() => Log.Info("DEMO: would open Windows Bluetooth settings");

    /// <summary>Applies a connection state to every device and keeps the contract order (connected first). Caller holds the lock.</summary>
    private void SetConnected(Func<BluetoothDeviceInfo, bool> connected) =>
        _devices = _devices
            .Select(d => d with { IsConnected = connected(d) })
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Id == HeadphonesId ? 0 : d.Id == MouseId ? 1 : 2)
            .ToArray();
}
