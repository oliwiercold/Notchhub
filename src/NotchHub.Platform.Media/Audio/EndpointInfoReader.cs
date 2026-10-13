using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Media.Audio.Interop;

namespace NotchHub.Platform.Media.Audio;

/// <summary>Reads render endpoints into <see cref="AudioDevice"/> records. Worker thread only.</summary>
internal static class EndpointInfoReader
{
    // EndpointFormFactor values (mmdeviceapi.h).
    private const uint FormFactorRemoteNetworkDevice = 0;
    private const uint FormFactorSpeakers = 1;
    private const uint FormFactorLineLevel = 2;
    private const uint FormFactorHeadphones = 3;
    private const uint FormFactorHeadset = 5;
    private const uint FormFactorHandset = 6;
    private const uint FormFactorDigitalAudioDisplayDevice = 9;

    private static readonly string[] BluetoothEnumerators = ["BTHENUM", "BTHHFENUM", "BTHLEDEVICE", "BTHLE", "BTHA2DP"];

    /// <summary>Id of the default render endpoint (multimedia role, falling back to console), or null.</summary>
    public static string? GetDefaultRenderId(IMMDeviceEnumerator enumerator)
    {
        foreach (var role in (ReadOnlySpan<ERole>)[ERole.Multimedia, ERole.Console])
        {
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, role, out var device) != CoreAudioConstants.S_OK) continue;
            try
            {
                if (device.GetId(out var id) == CoreAudioConstants.S_OK && !string.IsNullOrEmpty(id)) return id;
            }
            finally
            {
                ComRelease.Release(device);
            }
        }
        return null;
    }

    /// <summary>All active render endpoints, default first, then by name.</summary>
    public static IReadOnlyList<AudioDevice> ReadActiveOutputs(IMMDeviceEnumerator enumerator, string? defaultId)
    {
        var result = new List<AudioDevice>();
        if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out var collection) != CoreAudioConstants.S_OK)
            return result;
        try
        {
            if (collection.GetCount(out var count) != CoreAudioConstants.S_OK) return result;
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != CoreAudioConstants.S_OK) continue;
                try
                {
                    var info = TryRead(device, defaultId);
                    if (info is not null) result.Add(info);
                }
                finally
                {
                    ComRelease.Release(device);
                }
            }
        }
        finally
        {
            ComRelease.Release(collection);
        }

        result.Sort(static (a, b) =>
        {
            if (a.IsDefault != b.IsDefault) return a.IsDefault ? -1 : 1;
            var byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Id, b.Id);
        });
        return result;
    }

    /// <summary>Reads one endpoint; null when it vanished meanwhile.</summary>
    public static AudioDevice? TryRead(IMMDevice device, string? defaultId)
    {
        if (device.GetId(out var id) != CoreAudioConstants.S_OK || string.IsNullOrEmpty(id)) return null;
        if (device.OpenPropertyStore(CoreAudioConstants.StgmRead, out var store) != CoreAudioConstants.S_OK)
            return new AudioDevice(id, id, null, AudioDeviceKind.Other, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase));
        try
        {
            var friendly = PropertyStoreReader.GetString(store, PropertyKeys.DeviceFriendlyName);
            var name = PropertyStoreReader.GetString(store, PropertyKeys.DeviceDescription);
            var adapter = PropertyStoreReader.GetString(store, PropertyKeys.DeviceInterfaceFriendlyName);
            var formFactor = PropertyStoreReader.GetUInt32(store, PropertyKeys.AudioEndpointFormFactor);
            var enumeratorName = PropertyStoreReader.GetString(store, PropertyKeys.DeviceEnumeratorName);
            var devnode = PropertyStoreReader.GetString(store, PropertyKeys.EndpointDevnodeInstanceId);

            name = Clean(name) ?? Clean(friendly) ?? id;
            adapter = Clean(adapter) ?? AdapterFromFriendlyName(friendly);
            if (string.Equals(adapter, name, StringComparison.CurrentCultureIgnoreCase)) adapter = null;

            var kind = Classify(formFactor, enumeratorName, devnode);
            return new AudioDevice(id, name, adapter, kind, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Debug($"Audio: failed to read endpoint {id}: {ex.Message}");
            return new AudioDevice(id, id, null, AudioDeviceKind.Other, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ComRelease.Release(store);
        }
    }

    /// <summary>Bluetooth wins over the form factor (BT headphones report "Headphones"); USB is used for plain outputs.</summary>
    internal static AudioDeviceKind Classify(uint? formFactor, string? enumeratorName, string? devnodeInstanceId)
    {
        if (IsBluetooth(enumeratorName) || IsBluetooth(BusOf(devnodeInstanceId))) return AudioDeviceKind.Bluetooth;

        switch (formFactor)
        {
            case FormFactorHeadphones:
                return AudioDeviceKind.Headphones;
            case FormFactorHeadset or FormFactorHandset:
                return AudioDeviceKind.Headset;
            case FormFactorDigitalAudioDisplayDevice:
                return AudioDeviceKind.Hdmi;
        }

        var bus = enumeratorName ?? BusOf(devnodeInstanceId);
        if (string.Equals(bus, "USB", StringComparison.OrdinalIgnoreCase)) return AudioDeviceKind.Usb;

        return formFactor switch
        {
            FormFactorSpeakers or FormFactorLineLevel => AudioDeviceKind.Speakers,
            FormFactorRemoteNetworkDevice => AudioDeviceKind.Other,
            _ => AudioDeviceKind.Other,
        };
    }

    private static bool IsBluetooth(string? bus) =>
        bus is not null && BluetoothEnumerators.Any(b => string.Equals(b, bus, StringComparison.OrdinalIgnoreCase));

    /// <summary>"{1}.BTHENUM\{0000110b-…}…" → "BTHENUM".</summary>
    private static string? BusOf(string? devnodeInstanceId)
    {
        if (string.IsNullOrEmpty(devnodeInstanceId)) return null;
        var text = devnodeInstanceId.AsSpan();
        if (text.Length > 0 && text[0] == '{')
        {
            var close = text.IndexOf("}.");
            if (close >= 0) text = text[(close + 2)..];
        }
        var slash = text.IndexOf('\\');
        return slash > 0 ? text[..slash].ToString() : null;
    }

    /// <summary>"Speakers (Realtek(R) Audio)" → "Realtek(R) Audio".</summary>
    private static string? AdapterFromFriendlyName(string? friendly)
    {
        if (string.IsNullOrWhiteSpace(friendly) || !friendly.EndsWith(')')) return null;
        var depth = 0;
        for (var i = friendly.Length - 1; i >= 0; i--)
        {
            if (friendly[i] == ')') depth++;
            else if (friendly[i] == '(' && --depth == 0) return Clean(friendly[(i + 1)..^1]);
        }
        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
