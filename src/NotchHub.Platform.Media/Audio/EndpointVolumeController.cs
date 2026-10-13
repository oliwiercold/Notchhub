using System.Runtime.InteropServices;
using NotchHub.Core;
using NotchHub.Platform.Media.Audio.Interop;

namespace NotchHub.Platform.Media.Audio;

/// <summary>Master volume / mute of one endpoint with change notifications. Created and used on the worker thread.</summary>
internal sealed class EndpointVolumeController : IDisposable
{
    private readonly IAudioEndpointVolume _volume;
    private readonly Callback _callback;
    private bool _registered;
    private bool _disposed;

    private EndpointVolumeController(string deviceId, IAudioEndpointVolume volume, Action<EndpointVolumeController, AudioVolumeNotificationData> onNotify)
    {
        DeviceId = deviceId;
        _volume = volume;
        _callback = new Callback(data => onNotify(this, data));
    }

    public string DeviceId { get; }

    /// <summary>Activates IAudioEndpointVolume on <paramref name="device"/>; null when the endpoint has none.</summary>
    public static EndpointVolumeController? TryCreate(IMMDevice device, string deviceId,
        Action<EndpointVolumeController, AudioVolumeNotificationData> onNotify)
    {
        var hr = device.Activate(CoreAudioConstants.IID_IAudioEndpointVolume, CoreAudioConstants.ClsctxAll, 0, out var instance);
        if (hr < 0 || instance is not IAudioEndpointVolume volume)
        {
            Log.Warn($"Audio: IAudioEndpointVolume unavailable on {deviceId} (0x{hr:X8})");
            ComRelease.Release(instance);
            return null;
        }

        var controller = new EndpointVolumeController(deviceId, volume, onNotify);
        hr = volume.RegisterControlChangeNotify(controller._callback);
        if (hr < 0) Log.Warn($"Audio: volume notifications unavailable (0x{hr:X8}); volume will not update live");
        else controller._registered = true;
        return controller;
    }

    public bool TryRead(out float volume, out bool muted)
    {
        volume = 0;
        muted = false;
        if (_disposed) return false;
        return _volume.GetMasterVolumeLevelScalar(out volume) >= 0 & _volume.GetMute(out muted) >= 0;
    }

    /// <summary>Returns the HRESULT (AUDCLNT_E_DEVICE_INVALIDATED when the device vanished).</summary>
    public int SetVolume(float level, Guid eventContext) =>
        _disposed ? CoreAudioConstants.S_FALSE : _volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), eventContext);

    public int SetMute(bool muted, Guid eventContext) =>
        _disposed ? CoreAudioConstants.S_FALSE : _volume.SetMute(muted, eventContext);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered)
        {
            try { _volume.UnregisterControlChangeNotify(_callback); }
            catch (COMException ex) { Log.Debug($"Audio: unregister volume callback failed: {ex.Message}"); }
        }
        ComRelease.Release(_volume);
    }

    /// <summary>COM sink; runs on an audio-service thread and must return quickly.</summary>
    private sealed class Callback(Action<AudioVolumeNotificationData> handler) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notifyData)
        {
            try
            {
                if (notifyData != 0) handler(Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData));
            }
            catch (Exception ex)
            {
                Log.Error("Audio: volume notification handler failed", ex);
            }
            return CoreAudioConstants.S_OK;
        }
    }
}
