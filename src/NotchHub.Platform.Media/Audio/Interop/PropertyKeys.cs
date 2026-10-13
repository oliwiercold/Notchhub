using System.Runtime.InteropServices;

namespace NotchHub.Platform.Media.Audio.Interop;

/// <summary>Property keys read from audio endpoint property stores (functiondiscoverykeys_devpkey.h, mmdeviceapi.h).</summary>
internal static class PropertyKeys
{
    private static readonly Guid DeviceGroup = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");

    /// <summary>"Speakers (Realtek(R) Audio)".</summary>
    public static readonly PropertyKey DeviceFriendlyName = new(DeviceGroup, 14);

    /// <summary>"Speakers".</summary>
    public static readonly PropertyKey DeviceDescription = new(DeviceGroup, 2);

    /// <summary>Bus enumerator of the device ("HDAUDIO", "USB", "BTHENUM"…), when the endpoint store exposes it.</summary>
    public static readonly PropertyKey DeviceEnumeratorName = new(DeviceGroup, 24);

    /// <summary>"Realtek(R) Audio" — friendly name of the adapter (device interface).</summary>
    public static readonly PropertyKey DeviceInterfaceFriendlyName = new(new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), 2);

    /// <summary>EndpointFormFactor (Speakers, Headphones, Headset, DigitalAudioDisplayDevice…).</summary>
    public static readonly PropertyKey AudioEndpointFormFactor = new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);

    /// <summary>
    /// Instance id of the devnode the endpoint belongs to, prefixed with "{1}." (e.g. "{1}.BTHENUM\…",
    /// "{1}.HDAUDIO\FUNC_01…", "{1}.USB\VID_…"). Present on every render endpoint since Windows 7.
    /// </summary>
    public static readonly PropertyKey EndpointDevnodeInstanceId = new(new Guid("B3F8FA53-0004-438E-9003-51A46E139BFC"), 2);
}

/// <summary>Typed, exception-free reads from an <see cref="IPropertyStore"/>.</summary>
internal static class PropertyStoreReader
{
    public static string? GetString(IPropertyStore store, in PropertyKey key)
    {
        if (store.GetValue(key, out var value) != CoreAudioConstants.S_OK) return null;
        try { return value.AsString(); }
        finally { value.Clear(); }
    }

    public static uint? GetUInt32(IPropertyStore store, in PropertyKey key)
    {
        if (store.GetValue(key, out var value) != CoreAudioConstants.S_OK) return null;
        try { return value.AsUInt32(); }
        finally { value.Clear(); }
    }

    /// <summary>All properties as (key, readable value) pairs — diagnostics only.</summary>
    public static IReadOnlyList<(PropertyKey Key, string Value)> Dump(IPropertyStore store)
    {
        var result = new List<(PropertyKey, string)>();
        if (store.GetCount(out var count) != CoreAudioConstants.S_OK) return result;
        for (uint i = 0; i < count; i++)
        {
            if (store.GetAt(i, out var key) != CoreAudioConstants.S_OK) continue;
            if (store.GetValue(key, out var value) != CoreAudioConstants.S_OK) continue;
            try { result.Add((key, value.Describe())); }
            finally { value.Clear(); }
        }
        return result;
    }
}

/// <summary>Releases RCWs deterministically (COM objects of the audio stack hold device handles).</summary>
internal static class ComRelease
{
    public static void Release(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject)) return;
        try { Marshal.ReleaseComObject(comObject); }
        catch (Exception ex) when (ex is InvalidComObjectException or ArgumentException)
        {
            // already released / not an RCW: nothing to do
        }
    }
}
