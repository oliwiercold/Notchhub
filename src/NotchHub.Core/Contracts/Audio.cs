namespace NotchHub.Core.Contracts;

public enum AudioDeviceKind { Speakers, Headphones, Headset, Hdmi, Bluetooth, Usb, Other }

/// <summary>An audio output endpoint.</summary>
/// <param name="Name">Friendly name, e.g. "Speakers".</param>
/// <param name="Description">Adapter / device description, e.g. "Realtek(R) Audio".</param>
public sealed record AudioDevice(string Id, string Name, string? Description, AudioDeviceKind Kind, bool IsDefault);

/// <summary>Per-application volume (mixer) entry.</summary>
public sealed record AudioAppSession(string Id, string DisplayName, int ProcessId, float Volume, bool IsMuted, byte[]? IconPng);

public sealed class AudioVolumeChangedEventArgs(float volume, bool isMuted, bool isExternal) : EventArgs
{
    public float Volume { get; } = volume;
    public bool IsMuted { get; } = isMuted;
    /// <summary>True when the change did not originate from <see cref="IAudioService.SetVolume"/>/<see cref="IAudioService.SetMuted"/> (media keys, other apps) — used for the HUD.</summary>
    public bool IsExternal { get; } = isExternal;
}

public interface IAudioService : IBackgroundService
{
    bool IsAvailable { get; }
    /// <summary>Master volume of the default render device, 0..1.</summary>
    float Volume { get; }
    bool IsMuted { get; }
    AudioDevice? DefaultOutput { get; }
    IReadOnlyList<AudioDevice> Outputs { get; }
    /// <summary>Per-app sessions of the default device (may be empty).</summary>
    IReadOnlyList<AudioAppSession> Sessions { get; }

    /// <summary>Devices, default device, sessions, volume or mute changed.</summary>
    event EventHandler? Changed;
    /// <summary>Master volume or mute changed.</summary>
    event EventHandler<AudioVolumeChangedEventArgs>? VolumeChanged;

    void SetVolume(float volume);
    void SetMuted(bool muted);
    Task<bool> SetDefaultOutputAsync(string deviceId);
    void SetSessionVolume(string sessionId, float volume);
    void SetSessionMuted(string sessionId, bool muted);
    void OpenSettings();
}
