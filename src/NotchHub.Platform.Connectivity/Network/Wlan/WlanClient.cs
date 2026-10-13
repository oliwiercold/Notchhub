using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using NotchHub.Core;

namespace NotchHub.Platform.Connectivity.Network.Wlan;

/// <summary>A WLAN interface (adapter) known to the WLAN AutoConfig service.</summary>
internal readonly record struct WlanInterface(Guid Id, string Description, WlanInterfaceState State);

/// <summary>One row of <c>WlanGetAvailableNetworkList</c> (a network may appear once per saved profile + once without).</summary>
internal sealed record WlanNetworkEntry(
    string Ssid, byte[] SsidBytes, string ProfileName, int SignalQuality, bool IsSecured, bool IsConnected, bool HasProfile, Dot11BssType BssType,
    uint AuthAlgorithm = 0, uint CipherAlgorithm = 0);

/// <summary>A WLAN notification, copied out of native memory.</summary>
/// <param name="ReasonCode">WLAN_REASON_CODE for connection notifications (0 = success).</param>
/// <param name="Ssid">SSID for connection notifications.</param>
internal readonly record struct WlanEvent(uint Source, uint Code, Guid InterfaceId, uint? ReasonCode, string? Ssid);

/// <summary>
/// Thin, thread-safe managed wrapper over a wlanapi.dll client handle: interface enumeration, scans, network / BSS
/// lists, connect / disconnect, radio state and notifications. Native results are returned as Win32 error codes;
/// methods never throw for API failures. All native memory is freed with <c>WlanFreeMemory</c>.
/// </summary>
internal sealed unsafe class WlanClient : IDisposable
{
    private readonly object _gate = new();
    private IntPtr _handle;
    private GCHandle _self;
    private bool _notificationsRegistered;

    private WlanClient(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>Raised on a WLAN service thread. Handlers must be quick and must not call back into WLAN APIs synchronously.</summary>
    public event Action<WlanEvent>? Notification;

    /// <summary>Opens a client handle; null when the WLAN service is not running or wlanapi.dll is missing.</summary>
    public static WlanClient? TryOpen()
    {
        try
        {
            var rc = WlanNative.WlanOpenHandle(WlanNative.ClientVersion, IntPtr.Zero, out _, out var handle);
            if (rc != WlanNative.ErrorSuccess)
            {
                Log.Warn($"WlanClient: WlanOpenHandle failed ({rc}){(rc == WlanNative.ErrorServiceNotActive ? " - WLAN AutoConfig service not running" : "")}");
                return null;
            }

            var client = new WlanClient(handle);
            client.RegisterNotifications();
            return client;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("WlanClient: wlanapi.dll not available", ex);
            return null;
        }
    }

    private void RegisterNotifications()
    {
        lock (_gate)
        {
            _self = GCHandle.Alloc(this);
            var callback = (IntPtr)(delegate* unmanaged[Stdcall]<WlanNotificationData*, IntPtr, void>)&OnNativeNotification;
            var rc = WlanNative.WlanRegisterNotification(
                _handle, WlanNative.NotificationSourceAcm | WlanNative.NotificationSourceMsm, true,
                callback, GCHandle.ToIntPtr(_self), IntPtr.Zero, out _);
            if (rc == WlanNative.ErrorSuccess)
            {
                _notificationsRegistered = true;
            }
            else
            {
                Log.Warn($"WlanClient: WlanRegisterNotification failed ({rc}); scan completion falls back to timeouts");
                _self.Free();
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnNativeNotification(WlanNotificationData* data, IntPtr context)
    {
        // Exceptions must never cross back into native code.
        try
        {
            if (data is null || context == IntPtr.Zero) return;
            if (GCHandle.FromIntPtr(context).Target is not WlanClient client) return;

            uint? reason = null;
            string? ssid = null;
            if (data->NotificationSource == WlanNative.NotificationSourceAcm
                && data->NotificationCode is WlanNative.AcmConnectionComplete or WlanNative.AcmConnectionAttemptFail
                && data->Data != IntPtr.Zero
                && data->DataSize >= (uint)sizeof(WlanConnectionNotificationData))
            {
                var conn = (WlanConnectionNotificationData*)data->Data;
                reason = conn->ReasonCode;
                ssid = DecodeSsid(&conn->Ssid, out _);
            }

            client.Notification?.Invoke(new WlanEvent(data->NotificationSource, data->NotificationCode, data->InterfaceGuid, reason, ssid));
        }
        catch (Exception ex)
        {
            Log.Error("WlanClient: notification handler failed", ex);
        }
    }

    public IReadOnlyList<WlanInterface> GetInterfaces()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return [];
            var rc = WlanNative.WlanEnumInterfaces(_handle, IntPtr.Zero, out var list);
            if (rc != WlanNative.ErrorSuccess || list == IntPtr.Zero)
            {
                Log.Warn($"WlanClient: WlanEnumInterfaces failed ({rc})");
                return [];
            }

            try
            {
                var count = *(uint*)list;
                var items = (WlanInterfaceInfo*)((byte*)list + 8);
                var result = new List<WlanInterface>((int)count);
                for (var i = 0; i < count; i++)
                {
                    var info = &items[i];
                    result.Add(new WlanInterface(info->InterfaceGuid, new string(info->Description).TrimEnd('\0'), info->State));
                }
                return result;
            }
            finally
            {
                WlanNative.WlanFreeMemory(list);
            }
        }
    }

    /// <summary>Requests a scan; completion is reported by <see cref="WlanNative.AcmScanComplete"/> / <see cref="WlanNative.AcmScanFail"/>.</summary>
    public uint StartScan(Guid interfaceId)
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;
            return WlanNative.WlanScan(_handle, interfaceId, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public uint GetAvailableNetworks(Guid interfaceId, out List<WlanNetworkEntry> networks)
    {
        networks = [];
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;
            var rc = WlanNative.WlanGetAvailableNetworkList(_handle, interfaceId, 0, IntPtr.Zero, out var list);
            if (rc != WlanNative.ErrorSuccess || list == IntPtr.Zero) return rc;

            try
            {
                var count = *(uint*)list;
                var items = (WlanAvailableNetwork*)((byte*)list + 8);
                for (var i = 0; i < count; i++)
                {
                    var n = &items[i];
                    var ssid = DecodeSsid(&n->Ssid, out var raw);
                    networks.Add(new WlanNetworkEntry(
                        ssid,
                        raw,
                        new string(n->ProfileName).TrimEnd('\0'),
                        (int)Math.Clamp(n->SignalQuality, 0u, 100u),
                        n->SecurityEnabled != 0,
                        (n->Flags & WlanNative.AvailableNetworkConnected) != 0,
                        (n->Flags & WlanNative.AvailableNetworkHasProfile) != 0,
                        n->BssType,
                        n->DefaultAuthAlgorithm,
                        n->DefaultCipherAlgorithm));
                }
                return WlanNative.ErrorSuccess;
            }
            finally
            {
                WlanNative.WlanFreeMemory(list);
            }
        }
    }

    /// <summary>SSID → channel center frequency (kHz) of its strongest visible BSS. Empty on failure.</summary>
    public Dictionary<string, uint> GetStrongestBssFrequencies(Guid interfaceId)
    {
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return result;
            var rc = WlanNative.WlanGetNetworkBssList(_handle, interfaceId, IntPtr.Zero, Dot11BssType.Any, false, IntPtr.Zero, out var list);
            if (rc != WlanNative.ErrorSuccess || list == IntPtr.Zero) return result;

            try
            {
                var count = *(uint*)((byte*)list + 4);
                var items = (WlanBssEntry*)((byte*)list + 8);
                var bestQuality = new Dictionary<string, uint>(StringComparer.Ordinal);
                for (var i = 0; i < count; i++)
                {
                    var e = &items[i];
                    var ssid = DecodeSsid(&e->Ssid, out _);
                    if (ssid.Length == 0 || e->ChCenterFrequency == 0) continue;
                    if (bestQuality.TryGetValue(ssid, out var q) && q >= e->LinkQuality) continue;
                    bestQuality[ssid] = e->LinkQuality;
                    result[ssid] = e->ChCenterFrequency;
                }
            }
            finally
            {
                WlanNative.WlanFreeMemory(list);
            }
        }
        return result;
    }

    /// <summary>Connects using a saved profile (wlan_connection_mode_profile). Returns the Win32 result of the request.</summary>
    public uint Connect(Guid interfaceId, string profileName, byte[] ssid, Dot11BssType bssType)
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;

            var dot11 = default(Dot11Ssid);
            var len = Math.Min(ssid.Length, 32);
            dot11.Length = (uint)len;
            for (var i = 0; i < len; i++) dot11.Ssid[i] = ssid[i];

            fixed (char* profile = profileName)
            {
                var parameters = new WlanConnectionParameters
                {
                    ConnectionMode = WlanNative.ConnectionModeProfile,
                    Profile = profile,
                    Ssid = len > 0 ? &dot11 : null,
                    DesiredBssidList = IntPtr.Zero,
                    BssType = bssType == Dot11BssType.Independent ? Dot11BssType.Independent : Dot11BssType.Infrastructure,
                    Flags = 0,
                };
                return WlanNative.WlanConnect(_handle, interfaceId, &parameters, IntPtr.Zero);
            }
        }
    }

    /// <summary>Adds (or overwrites) a per-user profile. <paramref name="reasonCode"/> is the WLAN_REASON_CODE for invalid XML.</summary>
    public uint SetProfile(Guid interfaceId, string profileXml, out uint reasonCode)
    {
        reasonCode = 0;
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;
            return WlanNative.WlanSetProfile(_handle, interfaceId, WlanNative.ProfileUser, profileXml, null, true, IntPtr.Zero, out reasonCode);
        }
    }

    /// <summary>
    /// Reads a saved profile's XML (key material stays DPAPI-protected, which WlanSetProfile accepts back on this machine)
    /// and its WLAN_PROFILE_* flags. Returns the Win32 result; ERROR_NOT_FOUND (1168) when no such profile exists.
    /// </summary>
    public uint GetProfile(Guid interfaceId, string profileName, out string? profileXml, out uint flags)
    {
        profileXml = null;
        flags = 0;
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;
            var rc = WlanNative.WlanGetProfile(_handle, interfaceId, profileName, IntPtr.Zero, out var xml, ref flags, out _);
            if (rc != WlanNative.ErrorSuccess || xml == IntPtr.Zero) return rc;
            try
            {
                profileXml = Marshal.PtrToStringUni(xml);
            }
            finally
            {
                WlanNative.WlanFreeMemory(xml);
            }
            return rc;
        }
    }

    /// <summary>Writes a profile back with its original flags (used to restore a profile after a failed attempt).</summary>
    public uint RestoreProfile(Guid interfaceId, string profileXml, uint flags, out uint reasonCode)
    {
        reasonCode = 0;
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;
            return WlanNative.WlanSetProfile(_handle, interfaceId, flags & WlanNative.ProfileUser, profileXml, null, true, IntPtr.Zero, out reasonCode);
        }
    }

    public uint DeleteProfile(Guid interfaceId, string profileName)
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;
            return WlanNative.WlanDeleteProfile(_handle, interfaceId, profileName, IntPtr.Zero);
        }
    }

    public uint Disconnect(Guid interfaceId)
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return WlanNative.ErrorServiceNotActive;
            return WlanNative.WlanDisconnect(_handle, interfaceId, IntPtr.Zero);
        }
    }

    /// <summary>True when any PHY of the interface has both software and hardware radio on; null when unknown.</summary>
    public bool? QueryRadioOn(Guid interfaceId)
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return null;
            var rc = WlanNative.WlanQueryInterface(_handle, interfaceId, WlanNative.IntfOpcodeRadioState, IntPtr.Zero, out var size, out var data, out _);
            if (rc != WlanNative.ErrorSuccess || data == IntPtr.Zero) return null;

            try
            {
                if (size < 4) return null;
                var phys = Math.Min(*(uint*)data, (size - 4) / (uint)sizeof(WlanPhyRadioState));
                var states = (WlanPhyRadioState*)((byte*)data + 4);
                for (var i = 0; i < phys; i++)
                {
                    if (states[i].SoftwareRadioState == WlanNative.Dot11RadioStateOn && states[i].HardwareRadioState == WlanNative.Dot11RadioStateOn)
                        return true;
                }
                return false;
            }
            finally
            {
                WlanNative.WlanFreeMemory(data);
            }
        }
    }

    /// <summary>Decodes an SSID (UTF-8 in practice; invalid sequences are replaced). Empty for hidden / redacted SSIDs.</summary>
    internal static string DecodeSsid(Dot11Ssid* ssid, out byte[] raw)
    {
        var len = (int)Math.Min(ssid->Length, 32u);
        raw = new byte[len];
        for (var i = 0; i < len; i++) raw[i] = ssid->Ssid[i];
        // Some drivers zero-pad; a run of NULs is not a meaningful SSID.
        var text = Encoding.UTF8.GetString(raw).TrimEnd('\0');
        return text;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return;
            try
            {
                if (_notificationsRegistered)
                {
                    // Blocks until in-flight callbacks have returned, so freeing the GCHandle afterwards is safe.
                    WlanNative.WlanRegisterNotification(_handle, WlanNative.NotificationSourceNone, true, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _);
                    _notificationsRegistered = false;
                }
                WlanNative.WlanCloseHandle(_handle, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Warn("WlanClient: closing handle failed", ex);
            }
            finally
            {
                _handle = IntPtr.Zero;
                if (_self.IsAllocated) _self.Free();
            }
        }
    }
}
