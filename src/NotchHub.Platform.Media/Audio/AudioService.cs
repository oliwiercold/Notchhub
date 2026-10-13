using System.Diagnostics;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Media.Audio.Interop;
using NotchHub.Platform.Media.Common;

namespace NotchHub.Platform.Media.Audio;

/// <summary>
/// Windows Core Audio: master volume / mute of the default render endpoint, output devices and per-app sessions.
/// Everything COM-related runs on one dedicated MTA worker thread; the service is driven purely by OS notifications
/// (no polling). Events fire on background threads.
/// </summary>
public sealed class AudioService : IAudioService
{
    private static readonly TimeSpan VolumeWriteInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan SettleGrace = TimeSpan.FromMilliseconds(250);

    /// <summary>Tags our own writes so notifications can tell them apart from media keys / other apps.</summary>
    private readonly Guid _eventContext = Guid.NewGuid();
    private readonly AudioWorker _worker = new("NotchHub audio");
    private readonly AppIdentityResolver _resolver = new();
    private readonly VolumeWriteThrottle _volumeThrottle;
    private readonly CoalescingTimer _deviceRefresh;
    private readonly CoalescingTimer _sessionResync;
    private readonly CoalescingTimer _changedNotifier;

    // Worker-thread state.
    private IMMDeviceEnumerator? _enumerator;
    private DeviceNotificationClient? _deviceClient;
    private volatile EndpointVolumeController? _endpoint;
    private AppSessionTracker? _sessions;
    private readonly Dictionary<string, float> _pendingSessionVolumes = new(StringComparer.OrdinalIgnoreCase);
    private bool _sessionFlushQueued;

    // Published snapshots (any thread).
    private volatile bool _available;
    private volatile float _volume;
    private volatile bool _muted;
    private volatile AudioDevice? _defaultOutput;
    private volatile IReadOnlyList<AudioDevice> _outputs = [];
    private volatile IReadOnlyList<AudioAppSession> _appSessions = [];
    private int _started;
    private int _disposed;

    /// <summary>Creates the service; nothing touches the audio stack until <see cref="StartAsync"/>.</summary>
    public AudioService()
    {
        _volumeThrottle = new VolumeWriteThrottle(VolumeWriteInterval, v => _worker.Post(() => WriteVolume(v)));
        _deviceRefresh = new CoalescingTimer(TimeSpan.FromMilliseconds(150), () => _worker.Post(RefreshDevices), "Audio: device refresh");
        _sessionResync = new CoalescingTimer(TimeSpan.FromMilliseconds(200), () => _worker.Post(() => _sessions?.Resync()), "Audio: session resync");
        _changedNotifier = new CoalescingTimer(TimeSpan.FromMilliseconds(30), () => Changed?.Invoke(this, EventArgs.Empty), "Audio: Changed handler");
    }

    /// <inheritdoc />
    public bool IsAvailable => _available;
    /// <inheritdoc />
    public float Volume => _volume;
    /// <inheritdoc />
    public bool IsMuted => _muted;
    /// <inheritdoc />
    public AudioDevice? DefaultOutput => _defaultOutput;
    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> Outputs => _outputs;
    /// <inheritdoc />
    public IReadOnlyList<AudioAppSession> Sessions => _appSessions;

    /// <inheritdoc />
    public event EventHandler? Changed;
    /// <inheritdoc />
    public event EventHandler<AudioVolumeChangedEventArgs>? VolumeChanged;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return Task.CompletedTask;
        return _worker.InvokeAsync(() =>
        {
            try
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
                _deviceClient = new DeviceNotificationClient(_ => _deviceRefresh.Trigger());
                var hr = _enumerator.RegisterEndpointNotificationCallback(_deviceClient);
                if (hr < 0) Log.Warn($"Audio: device notifications unavailable (0x{hr:X8})");
                RefreshDevices();
            }
            catch (Exception ex)
            {
                Log.Error("Audio: initialization failed", ex);
            }
            return true;
        });
    }

    /// <summary>Worker: re-reads the device list and rebinds volume / sessions when the default endpoint changed.</summary>
    private void RefreshDevices()
    {
        if (_enumerator is null) return;
        var defaultId = EndpointInfoReader.GetDefaultRenderId(_enumerator);
        _outputs = EndpointInfoReader.ReadActiveOutputs(_enumerator, defaultId);
        _defaultOutput = defaultId is null ? null : _outputs.FirstOrDefault(d => d.Id == defaultId);

        if (_endpoint?.DeviceId != defaultId)
        {
            _endpoint?.Dispose();
            _endpoint = null;
            _sessions?.Dispose();
            _sessions = null;
            _appSessions = [];

            if (defaultId is not null && _enumerator.GetDevice(defaultId, out var device) == CoreAudioConstants.S_OK)
            {
                try
                {
                    _endpoint = EndpointVolumeController.TryCreate(device, defaultId, OnEndpointNotify);
                    _sessions = AppSessionTracker.TryCreate(device, defaultId, _worker, _resolver, _eventContext,
                        OnSessionsChanged, _sessionResync.Trigger);
                }
                finally
                {
                    ComRelease.Release(device);
                }
            }
            if (_sessions is not null) _appSessions = _sessions.Snapshot;
        }

        _available = _endpoint is not null;
        if (_endpoint is not null && _endpoint.TryRead(out var volume, out var muted))
        {
            _volume = volume;
            _muted = muted;
        }
        _changedNotifier.Trigger();
    }

    private void OnSessionsChanged()
    {
        if (_sessions is not null) _appSessions = _sessions.Snapshot;
        _changedNotifier.Trigger();
    }

    /// <summary>Audio-service thread: master volume / mute changed.</summary>
    private void OnEndpointNotify(EndpointVolumeController source, AudioVolumeNotificationData data)
    {
        if (!ReferenceEquals(source, _endpoint) || Volatile.Read(ref _disposed) != 0) return;
        var external = data.EventContext != _eventContext;
        var muted = data.Muted != 0;
        var volume = Math.Clamp(data.MasterVolume, 0f, 1f);
        // While a slider drag is still being written, intermediate echoes of our own writes must not make the
        // published value jump back; keep the optimistic value.
        if (external || !_volumeThrottle.IsSettling(SettleGrace)) _volume = volume;
        _muted = muted;
        try
        {
            VolumeChanged?.Invoke(this, new AudioVolumeChangedEventArgs(_volume, muted, external));
        }
        catch (Exception ex)
        {
            Log.Error("Audio: VolumeChanged handler failed", ex);
        }
        _changedNotifier.Trigger();
    }

    /// <inheritdoc />
    public void SetVolume(float volume)
    {
        if (float.IsNaN(volume)) return;
        volume = Math.Clamp(volume, 0f, 1f);
        _volume = volume; // optimistic
        _volumeThrottle.Request(volume);
    }

    private void WriteVolume(float volume)
    {
        if (_endpoint is null) return;
        var hr = _endpoint.SetVolume(volume, _eventContext);
        if (hr == CoreAudioConstants.AUDCLNT_E_DEVICE_INVALIDATED) _deviceRefresh.Trigger();
        else if (hr < 0) Log.Warn($"Audio: SetMasterVolumeLevelScalar failed (0x{hr:X8})");
    }

    /// <inheritdoc />
    public void SetMuted(bool muted)
    {
        _muted = muted; // optimistic
        _worker.Post(() =>
        {
            if (_endpoint is null) return;
            var hr = _endpoint.SetMute(muted, _eventContext);
            if (hr == CoreAudioConstants.AUDCLNT_E_DEVICE_INVALIDATED) _deviceRefresh.Trigger();
            else if (hr < 0) Log.Warn($"Audio: SetMute failed (0x{hr:X8})");
        });
    }

    /// <inheritdoc />
    public Task<bool> SetDefaultOutputAsync(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return Task.FromResult(false);
        return _worker.InvokeAsync(() =>
        {
            IPolicyConfig? policy = null;
            try
            {
                policy = (IPolicyConfig)new PolicyConfigCoClass();
                var ok = true;
                foreach (var role in (ReadOnlySpan<ERole>)[ERole.Console, ERole.Multimedia, ERole.Communications])
                {
                    var hr = policy.SetDefaultEndpoint(deviceId, role);
                    if (hr < 0)
                    {
                        Log.Warn($"Audio: SetDefaultEndpoint({role}) failed (0x{hr:X8})");
                        ok = false;
                    }
                }
                _deviceRefresh.Trigger();
                return ok;
            }
            catch (Exception ex)
            {
                Log.Error("Audio: switching the default output failed", ex);
                return false;
            }
            finally
            {
                ComRelease.Release(policy);
            }
        });
    }

    /// <inheritdoc />
    public void SetSessionVolume(string sessionId, float volume)
    {
        if (string.IsNullOrEmpty(sessionId) || float.IsNaN(volume)) return;
        // Latest value per app wins; one queued flush applies whatever is pending (natural throttling for drags).
        _worker.Post(() =>
        {
            _pendingSessionVolumes[sessionId] = volume;
            if (_sessionFlushQueued) return;
            _sessionFlushQueued = true;
            _worker.Post(FlushSessionVolumes);
        });
    }

    private void FlushSessionVolumes()
    {
        _sessionFlushQueued = false;
        foreach (var (id, volume) in _pendingSessionVolumes) _sessions?.SetVolume(id, volume);
        _pendingSessionVolumes.Clear();
    }

    /// <inheritdoc />
    public void SetSessionMuted(string sessionId, bool muted)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _worker.Post(() => _sessions?.SetMuted(sessionId, muted));
    }

    /// <inheritdoc />
    public void OpenSettings()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Audio: could not open sound settings", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _volumeThrottle.Dispose();
        _deviceRefresh.Dispose();
        _sessionResync.Dispose();
        _changedNotifier.Dispose();
        _worker.Post(() =>
        {
            try
            {
                if (_enumerator is not null && _deviceClient is not null) _enumerator.UnregisterEndpointNotificationCallback(_deviceClient);
                _endpoint?.Dispose();
                _sessions?.Dispose();
                ComRelease.Release(_enumerator);
            }
            catch (Exception ex)
            {
                Log.Debug($"Audio: teardown: {ex.Message}");
            }
            _endpoint = null;
            _sessions = null;
            _enumerator = null;
        });
        _worker.Dispose();
    }
}
