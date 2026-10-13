using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;
using NotchHub.Demo.Infrastructure;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo audio: master volume 64 % on "Speakers (Realtek(R) Audio)", Bluetooth headphones and an HDMI monitor as other
/// outputs, and three per-app mixer sessions. With live events, an external volume change (media keys) happens about
/// once a minute so the volume HUD can be seen.
/// </summary>
public sealed class FakeAudioService : FakeBackgroundService, IAudioService
{
    private const float ExternalStep = 0.04f; // two media-key presses (2 % each)

    private readonly Random _random = DemoRandom.For("audio");
    private float _volume = 0.64f;
    private bool _muted;
    private int _externalDirection = 1;
    private AudioDevice[] _outputs =
    [
        new("{0.0.0.00000000}.{6b0d7a2e-3f4c-4e8a-9d1b-2c5e8f7a1b30}", "Speakers", "Realtek(R) Audio", AudioDeviceKind.Speakers, true),
        new("{0.0.0.00000000}.{a41f9c03-7d2e-4b6a-8c15-9e3d2f6b4a71}", "WH-1000XM4", "Sony WH-1000XM4 (Bluetooth)", AudioDeviceKind.Bluetooth, false),
        new("{0.0.0.00000000}.{d93e5b18-1c7a-4f20-b6e4-5a8c3d9f0e52}", "LG ULTRAWIDE", "NVIDIA High Definition Audio", AudioDeviceKind.Hdmi, false),
    ];
    private AudioAppSession[] _sessions =
    [
        new("demo-session-spotify", "Spotify", 14212, 1.0f, false, null),
        new("demo-session-chrome", "Google Chrome", 9876, 0.80f, false, null),
        new("demo-session-discord", "Discord", 11340, 0.55f, false, null),
    ];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<AudioVolumeChangedEventArgs>? VolumeChanged;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public float Volume
    {
        get { lock (Gate) return _volume; }
    }

    /// <inheritdoc />
    public bool IsMuted
    {
        get { lock (Gate) return _muted; }
    }

    /// <inheritdoc />
    public AudioDevice? DefaultOutput
    {
        get { lock (Gate) return _outputs.FirstOrDefault(d => d.IsDefault); }
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> Outputs
    {
        get { lock (Gate) return _outputs; }
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioAppSession> Sessions
    {
        get { lock (Gate) return _sessions; }
    }

    /// <inheritdoc />
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        IconSpec[] specs = [DemoIcons.MixerSpotify, DemoIcons.MixerChrome, DemoIcons.MixerDiscord];
        var icons = await Task.WhenAll(specs.Select(DemoAssets.IconAsync)).ConfigureAwait(false);
        lock (Gate) _sessions = _sessions.Select((s, i) => s with { IconPng = icons[i] }).ToArray();
        Raise(Changed);

        StartLiveLoop("audio", NextExternalDelay, () =>
        {
            float target;
            lock (Gate)
            {
                // Alternate up / down so the level stays around 64 %.
                target = Math.Clamp(_volume + _externalDirection * ExternalStep, 0f, 1f);
                _externalDirection = -_externalDirection;
            }
            SimulateExternalVolumeChange(target);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void SetVolume(float volume) => ApplyVolume(volume, null, isExternal: false);

    /// <inheritdoc />
    public void SetMuted(bool muted) => ApplyVolume(null, muted, isExternal: false);

    /// <summary>Simulates a change made outside NotchHub (media keys, another app): raises VolumeChanged with isExternal = true.</summary>
    public void SimulateExternalVolumeChange(float volume, bool? muted = null) => ApplyVolume(volume, muted, isExternal: true);

    /// <inheritdoc />
    public async Task<bool> SetDefaultOutputAsync(string deviceId)
    {
        await Task.Delay(120).ConfigureAwait(false); // endpoint switches are not instant
        lock (Gate)
        {
            if (_outputs.All(d => d.Id != deviceId)) return false;
            if (_outputs.Any(d => d.Id == deviceId && d.IsDefault)) return true;
            _outputs = _outputs.Select(d => d with { IsDefault = d.Id == deviceId }).ToArray();
        }
        Log.Info($"DEMO: default audio output -> {deviceId}");
        Raise(Changed);
        return true;
    }

    /// <inheritdoc />
    public void SetSessionVolume(string sessionId, float volume)
    {
        if (float.IsNaN(volume)) return;
        UpdateSession(sessionId, s => s with { Volume = Math.Clamp(volume, 0f, 1f) });
    }

    /// <inheritdoc />
    public void SetSessionMuted(string sessionId, bool muted) => UpdateSession(sessionId, s => s with { IsMuted = muted });

    /// <inheritdoc />
    public void OpenSettings() => Log.Info("DEMO: would open Windows sound settings");

    private void ApplyVolume(float? volume, bool? muted, bool isExternal)
    {
        if (volume is { } v && float.IsNaN(v)) return;
        float newVolume;
        bool newMuted;
        lock (Gate)
        {
            newVolume = volume is { } value ? Math.Clamp(value, 0f, 1f) : _volume;
            newMuted = muted ?? _muted;
            if (Math.Abs(newVolume - _volume) < 0.0005f && newMuted == _muted) return;
            _volume = newVolume;
            _muted = newMuted;
        }
        Raise(VolumeChanged, new AudioVolumeChangedEventArgs(newVolume, newMuted, isExternal));
        Raise(Changed);
    }

    private void UpdateSession(string sessionId, Func<AudioAppSession, AudioAppSession> update)
    {
        lock (Gate)
        {
            var index = Array.FindIndex(_sessions, s => s.Id == sessionId);
            if (index < 0) return;
            var updated = update(_sessions[index]);
            if (updated == _sessions[index]) return;
            var copy = (AudioAppSession[])_sessions.Clone();
            copy[index] = updated;
            _sessions = copy;
        }
        Raise(Changed);
    }

    private TimeSpan NextExternalDelay()
    {
        lock (Gate) return TimeSpan.FromSeconds(55 + _random.NextDouble() * 10);
    }
}
