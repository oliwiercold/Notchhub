using System.Runtime.InteropServices;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Media.Audio.Interop;
using NotchHub.Platform.Media.Common;

namespace NotchHub.Platform.Media.Audio;

/// <summary>
/// Per-application audio sessions of one render endpoint. Created, used and disposed on the <see cref="AudioWorker"/>
/// thread; COM callbacks only post work back to it. Sessions of the same app (same exe / package) are merged into one
/// <see cref="AudioAppSession"/> whose id is the app key, so the mixer shows one row per app.
/// </summary>
internal sealed class AppSessionTracker : IDisposable
{
    private readonly AudioWorker _worker;
    private readonly AppIdentityResolver _resolver;
    private readonly Guid _eventContext;
    private readonly Action _changed;
    private readonly Action _resyncRequested;
    private readonly IAudioSessionManager2 _manager;
    private readonly SessionCreatedSink _createdSink;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _notificationsRegistered;
    private bool _disposed;

    private AppSessionTracker(AudioWorker worker, AppIdentityResolver resolver, Guid eventContext, IAudioSessionManager2 manager,
        Action changed, Action resyncRequested)
    {
        _worker = worker;
        _resolver = resolver;
        _eventContext = eventContext;
        _manager = manager;
        _changed = changed;
        _resyncRequested = resyncRequested;
        _createdSink = new SessionCreatedSink(resyncRequested);
    }

    public string DeviceId { get; private set; } = "";

    /// <summary>Latest merged snapshot (immutable).</summary>
    public IReadOnlyList<AudioAppSession> Snapshot { get; private set; } = [];

    public static AppSessionTracker? TryCreate(IMMDevice device, string deviceId, AudioWorker worker, AppIdentityResolver resolver,
        Guid eventContext, Action changed, Action resyncRequested)
    {
        var hr = device.Activate(CoreAudioConstants.IID_IAudioSessionManager2, CoreAudioConstants.ClsctxAll, 0, out var instance);
        if (hr < 0 || instance is not IAudioSessionManager2 manager)
        {
            Log.Warn($"Audio: IAudioSessionManager2 unavailable (0x{hr:X8})");
            ComRelease.Release(instance);
            return null;
        }

        var tracker = new AppSessionTracker(worker, resolver, eventContext, manager, changed, resyncRequested) { DeviceId = deviceId };
        // The session list must be enumerated once before registering, otherwise OnSessionCreated never fires.
        tracker.Resync();
        hr = manager.RegisterSessionNotification(tracker._createdSink);
        if (hr < 0) Log.Warn($"Audio: session notifications unavailable (0x{hr:X8})");
        else tracker._notificationsRegistered = true;
        return tracker;
    }

    /// <summary>Re-enumerates the sessions, (un)subscribing as needed, and rebuilds <see cref="Snapshot"/>.</summary>
    public void Resync()
    {
        if (_disposed) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (_manager.GetSessionEnumerator(out var enumerator) == CoreAudioConstants.S_OK)
        {
            try
            {
                if (enumerator.GetCount(out var count) == CoreAudioConstants.S_OK)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (enumerator.GetSession(i, out var control) != CoreAudioConstants.S_OK) continue;
                        var keep = false;
                        try
                        {
                            keep = TryTrack(control, seen);
                        }
                        catch (Exception ex) when (ex is COMException or InvalidCastException)
                        {
                            Log.Debug($"Audio: session skipped: {ex.Message}");
                        }
                        finally
                        {
                            if (!keep) ComRelease.Release(control);
                        }
                    }
                }
            }
            finally
            {
                ComRelease.Release(enumerator);
            }
        }

        foreach (var id in _entries.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _entries[id].Dispose();
            _entries.Remove(id);
        }
        Publish();
    }

    /// <summary>Returns true when <paramref name="control"/> is now owned by a new entry.</summary>
    private bool TryTrack(IAudioSessionControl control, HashSet<string> seen)
    {
        var control2 = (IAudioSessionControl2)control;
        if (control2.GetSessionInstanceIdentifier(out var instanceId) < 0 || string.IsNullOrEmpty(instanceId)) return false;
        if (control2.GetState(out var state) < 0 || state == AudioSessionState.Expired) return false;
        control2.GetProcessId(out var pid);
        if (pid != 0 && !ProcessImage.IsAlive(pid)) return false;

        seen.Add(instanceId);
        if (_entries.TryGetValue(instanceId, out var existing))
        {
            existing.State = state;
            return false;
        }

        control2.GetDisplayName(out var displayName);
        control2.GetIconPath(out var iconPath);
        var identity = control2.IsSystemSoundsSession() == CoreAudioConstants.S_OK
            ? _resolver.ResolveSystemSounds(displayName, iconPath)
            : _resolver.Resolve(pid, displayName, iconPath);

        var volume = (ISimpleAudioVolume)control;
        volume.GetMasterVolume(out var level);
        volume.GetMute(out var muted);

        var entry = new Entry(instanceId, control, volume, identity) { ProcessId = pid, State = state, Volume = level, Muted = muted };
        entry.Sink = new SessionEventsSink(this, instanceId);
        if (control.RegisterAudioSessionNotification(entry.Sink) >= 0) entry.SinkRegistered = true;
        entry.ExitWatcher = ProcessExitWatcher.TryWatch(pid, _resyncRequested, "Audio: session process exit");
        _entries[instanceId] = entry;
        return true;
    }

    private void OnSessionVolume(string instanceId, float volume, bool muted)
    {
        if (_disposed || !_entries.TryGetValue(instanceId, out var entry)) return;
        entry.Volume = volume;
        entry.Muted = muted;
        Publish();
    }

    private void OnSessionState(string instanceId, AudioSessionState state)
    {
        if (_disposed || !_entries.TryGetValue(instanceId, out var entry)) return;
        if (state == AudioSessionState.Expired) { _resyncRequested(); return; }
        entry.State = state;
        Publish();
    }

    /// <summary>Rebuilds the merged snapshot and signals a change.</summary>
    public void Publish()
    {
        if (_disposed) return;
        var groups = _entries.Values.GroupBy(e => e.Identity.Key, StringComparer.OrdinalIgnoreCase);
        var list = new List<(bool Active, AudioAppSession Session)>();
        foreach (var group in groups)
        {
            // Prefer an active session as the representative (its volume is what the user hears).
            var rep = group.OrderByDescending(e => e.State == AudioSessionState.Active).First();
            var icon = _resolver.GetIcon(rep.Identity, () => _worker.Post(Publish));
            list.Add((group.Any(e => e.State == AudioSessionState.Active),
                new AudioAppSession(rep.Identity.Key, rep.Identity.DisplayName, (int)rep.ProcessId, rep.Volume, rep.Muted, icon)));
        }

        Snapshot = list
            .OrderByDescending(x => x.Active)
            .ThenBy(x => x.Session.Id == "system")
            .ThenBy(x => x.Session.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Session)
            .ToArray();
        _changed();
    }

    public void SetVolume(string appKey, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        foreach (var entry in Matching(appKey))
        {
            if (entry.SimpleVolume.SetMasterVolume(volume, _eventContext) >= 0) entry.Volume = volume;
        }
        Publish();
    }

    public void SetMuted(string appKey, bool muted)
    {
        foreach (var entry in Matching(appKey))
        {
            if (entry.SimpleVolume.SetMute(muted, _eventContext) >= 0) entry.Muted = muted;
        }
        Publish();
    }

    private IEnumerable<Entry> Matching(string appKey) =>
        _entries.Values.Where(e => string.Equals(e.Identity.Key, appKey, StringComparison.OrdinalIgnoreCase)).ToList();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_notificationsRegistered)
        {
            try { _manager.UnregisterSessionNotification(_createdSink); }
            catch (COMException ex) { Log.Debug($"Audio: unregister session notification failed: {ex.Message}"); }
        }
        foreach (var entry in _entries.Values) entry.Dispose();
        _entries.Clear();
        ComRelease.Release(_manager);
        Snapshot = [];
    }

    private sealed class Entry(string instanceId, IAudioSessionControl control, ISimpleAudioVolume simpleVolume, AppIdentity identity) : IDisposable
    {
        public string InstanceId { get; } = instanceId;
        public IAudioSessionControl Control { get; } = control;
        public ISimpleAudioVolume SimpleVolume { get; } = simpleVolume;
        public AppIdentity Identity { get; } = identity;
        public uint ProcessId { get; init; }
        public AudioSessionState State { get; set; }
        public float Volume { get; set; }
        public bool Muted { get; set; }
        public SessionEventsSink? Sink { get; set; }
        public bool SinkRegistered { get; set; }
        public ProcessExitWatcher? ExitWatcher { get; set; }

        public void Dispose()
        {
            ExitWatcher?.Dispose();
            if (SinkRegistered && Sink is not null)
            {
                try { Control.UnregisterAudioSessionNotification(Sink); }
                catch (COMException) { /* session already gone */ }
            }
            ComRelease.Release(Control);
        }
    }

    /// <summary>Per-session COM sink; runs on audio-service threads and only posts to the worker.</summary>
    private sealed class SessionEventsSink(AppSessionTracker owner, string instanceId) : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string? newDisplayName, nint eventContext) => CoreAudioConstants.S_OK;
        public int OnIconPathChanged(string? newIconPath, nint eventContext) => CoreAudioConstants.S_OK;

        public int OnSimpleVolumeChanged(float newVolume, bool newMute, nint eventContext)
        {
            owner._worker.Post(() => owner.OnSessionVolume(instanceId, newVolume, newMute));
            return CoreAudioConstants.S_OK;
        }

        public int OnChannelVolumeChanged(uint channelCount, nint newChannelVolumeArray, uint changedChannel, nint eventContext) => CoreAudioConstants.S_OK;
        public int OnGroupingParamChanged(nint newGroupingParam, nint eventContext) => CoreAudioConstants.S_OK;

        public int OnStateChanged(AudioSessionState newState)
        {
            owner._worker.Post(() => owner.OnSessionState(instanceId, newState));
            return CoreAudioConstants.S_OK;
        }

        public int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            owner._resyncRequested();
            return CoreAudioConstants.S_OK;
        }
    }

    private sealed class SessionCreatedSink(Action created) : IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            // Our own enumeration picks the session up; the RCW passed here is not kept.
            created();
            return CoreAudioConstants.S_OK;
        }
    }
}
