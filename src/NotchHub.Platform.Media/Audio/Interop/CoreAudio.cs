using System.Runtime.InteropServices;

// Classic [ComImport] definitions of the Windows Core Audio API (mmdeviceapi.h, endpointvolume.h, audiopolicy.h).
// Notes that matter for correctness:
//  * C# does not flatten COM interface inheritance, so derived interfaces re-declare every base method in vtable order.
//  * String parameters are LPWSTR (the COM default would be BSTR).
//  * Methods that legitimately fail at runtime (device gone, no default endpoint) use [PreserveSig] and are checked.
namespace NotchHub.Platform.Media.Audio.Interop;

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[Flags]
internal enum DeviceState : uint
{
    Active = 0x1,
    Disabled = 0x2,
    NotPresent = 0x4,
    Unplugged = 0x8,
    All = 0xF,
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

internal enum AudioSessionDisconnectReason
{
    DeviceRemoval = 0,
    ServerShutdown = 1,
    FormatChanged = 2,
    SessionLogoff = 3,
    SessionDisconnected = 4,
    ExclusiveModeOverride = 5,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, int propertyId) : IEquatable<PropertyKey>
{
    public readonly Guid FormatId = formatId;
    public readonly int PropertyId = propertyId;

    public bool Equals(PropertyKey other) => FormatId == other.FormatId && PropertyId == other.PropertyId;
    public override bool Equals(object? obj) => obj is PropertyKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(FormatId, PropertyId);
    public override string ToString() => $"{FormatId:B},{PropertyId}";
}

/// <summary>Header of AUDIO_VOLUME_NOTIFICATION_DATA (the trailing per-channel array is not needed).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;
    public int Muted;
    public float MasterVolume;
    public uint Channels;
}

internal static class CoreAudioConstants
{
    public const uint ClsctxAll = 0x17;
    public const uint StgmRead = 0;

    public const int S_OK = 0;
    public const int S_FALSE = 1;
    /// <summary>HRESULT_FROM_WIN32(ERROR_NOT_FOUND): no default endpoint.</summary>
    public const int E_NOTFOUND = unchecked((int)0x80070490);
    public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);

    public static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCoClass;

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(in Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

    [PreserveSig]
    int OpenPropertyStore(uint access, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out DeviceState state);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(in PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(in PropertyKey key, in PropVariant value);

    [PreserveSig]
    int Commit();
}

/// <summary>Implemented by us; called by the audio service on its own (MTA) threads.</summary>
[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, DeviceState newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, in Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, in Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float levelDb, in Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channel, float level, in Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float levelDb);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channel, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

    [PreserveSig]
    int GetVolumeStepInfo(out uint step, out uint stepCount);

    [PreserveSig]
    int VolumeStepUp(in Guid eventContext);

    [PreserveSig]
    int VolumeStepDown(in Guid eventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

/// <summary>Implemented by us. The argument points to AUDIO_VOLUME_NOTIFICATION_DATA.</summary>
[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(nint notifyData);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig]
    int GetAudioSessionControl(nint sessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(nint sessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);

    // IAudioSessionManager2
    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

    [PreserveSig]
    int RegisterSessionNotification(IAudioSessionNotification sessionNotification);

    [PreserveSig]
    int UnregisterSessionNotification(IAudioSessionNotification sessionNotification);

    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, nint duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(nint duckNotification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int sessionCount);

    [PreserveSig]
    int GetSession(int sessionIndex, out IAudioSessionControl session);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? displayName);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, in Guid eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? iconPath);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, in Guid eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);

    [PreserveSig]
    int SetGroupingParam(in Guid groupingParam, in Guid eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents newNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents newNotifications);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? displayName);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, in Guid eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string? iconPath);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, in Guid eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);

    [PreserveSig]
    int SetGroupingParam(in Guid groupingParam, in Guid eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents newNotifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents newNotifications);

    // IAudioSessionControl2
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? sessionId);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? instanceId);

    /// <summary>May return AUDCLNT_S_NO_SINGLE_PROCESS (a success code) for cross-process sessions.</summary>
    [PreserveSig]
    int GetProcessId(out uint processId);

    /// <summary>S_OK for the system sounds session, S_FALSE otherwise.</summary>
    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float level, in Guid eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

/// <summary>Implemented by us; one sink per audio session.</summary>
[ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string? newDisplayName, nint eventContext);

    [PreserveSig]
    int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string? newIconPath, nint eventContext);

    [PreserveSig]
    int OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, nint eventContext);

    [PreserveSig]
    int OnChannelVolumeChanged(uint channelCount, nint newChannelVolumeArray, uint changedChannel, nint eventContext);

    [PreserveSig]
    int OnGroupingParamChanged(nint newGroupingParam, nint eventContext);

    [PreserveSig]
    int OnStateChanged(AudioSessionState newState);

    [PreserveSig]
    int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
}

/// <summary>Implemented by us; called when a new session is created on the device.</summary>
[ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IAudioSessionControl newSession);
}

/// <summary>
/// Undocumented policy interface used by the Windows sound settings to change the default endpoint
/// (Windows 10 / 11 layout, IID f8679f50-…). Only <see cref="SetDefaultEndpoint"/> and the read-only
/// <see cref="GetMixFormat"/> are ever called; the other slots exist solely to keep the vtable order right.
/// </summary>
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    /// <summary>Returns a CoTaskMem-allocated WAVEFORMATEX(EXTENSIBLE) that the caller frees.</summary>
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out nint format);

    [PreserveSig]
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, out nint format);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint endpointFormat, nint mixFormat);

    [PreserveSig]
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, nint defaultPeriodOut, nint minimumPeriodOut);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint period);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint shareMode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint shareMode);

    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, nint key, nint value);

    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, nint key, nint value);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}

/// <summary>CPolicyConfigClient.</summary>
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigCoClass;
