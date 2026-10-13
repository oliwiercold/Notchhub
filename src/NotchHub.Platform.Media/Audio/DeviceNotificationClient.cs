using NotchHub.Core;
using NotchHub.Platform.Media.Audio.Interop;

namespace NotchHub.Platform.Media.Audio;

/// <summary>
/// IMMNotificationClient sink. Called on audio-service threads, where blocking or calling back into the enumerator
/// is forbidden, so every callback only forwards a cheap "devices changed" signal.
/// </summary>
internal sealed class DeviceNotificationClient(Action<string> devicesChanged) : IMMNotificationClient
{
    private static readonly PropertyKey[] RelevantProperties =
    [
        PropertyKeys.DeviceFriendlyName,
        PropertyKeys.DeviceDescription,
        PropertyKeys.DeviceInterfaceFriendlyName,
        PropertyKeys.AudioEndpointFormFactor,
    ];

    public int OnDeviceStateChanged(string deviceId, DeviceState newState) => Notify($"state {newState}");

    public int OnDeviceAdded(string deviceId) => Notify("added");

    public int OnDeviceRemoved(string deviceId) => Notify("removed");

    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) =>
        flow == EDataFlow.Render && role != ERole.Communications ? Notify($"default {role}") : CoreAudioConstants.S_OK;

    public int OnPropertyValueChanged(string deviceId, PropertyKey key) =>
        Array.IndexOf(RelevantProperties, key) >= 0 ? Notify("property") : CoreAudioConstants.S_OK;

    private int Notify(string reason)
    {
        try
        {
            devicesChanged(reason);
        }
        catch (Exception ex)
        {
            Log.Error("Audio: device notification handler failed", ex);
        }
        return CoreAudioConstants.S_OK;
    }
}
