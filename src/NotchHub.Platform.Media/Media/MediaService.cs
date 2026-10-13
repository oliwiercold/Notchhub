using System.Collections.Concurrent;
using System.Security.Cryptography;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Media.Common;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Gsmtc = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace NotchHub.Platform.Media.Media;

/// <summary>
/// Windows media sessions (System Media Transport Controls: Spotify, browsers, players…). Purely event driven:
/// SMTC events update per-session snapshots and a debounced <see cref="Changed"/> is raised (background thread).
/// Progress is not polled; UIs extrapolate with <see cref="MediaSessionInfo.GetLivePosition"/>.
/// </summary>
public sealed partial class MediaService : IMediaService
{
    private const long MaxThumbnailBytes = 8 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, Tracker> _trackers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly CoalescingTimer _changedNotifier;
    private GsmtcManager? _manager;
    private string? _systemCurrentId;
    private string? _selectedId;
    private volatile IReadOnlyList<MediaSessionInfo> _sessions = [];
    private volatile MediaSessionInfo? _current;
    private int _started;
    private bool _disposed;

    /// <summary>Creates the service; SMTC is only touched in <see cref="StartAsync"/>.</summary>
    public MediaService()
    {
        _changedNotifier = new CoalescingTimer(TimeSpan.FromMilliseconds(60), () => Changed?.Invoke(this, EventArgs.Empty), "Media: Changed handler");
    }

    /// <inheritdoc />
    public MediaSessionInfo? Current => _current;
    /// <inheritdoc />
    public IReadOnlyList<MediaSessionInfo> Sessions => _sessions;
    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Log.Safe("Media: Spotify init", InitSpotify);
        try
        {
            var manager = await GsmtcManager.RequestAsync().AsTask(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed) return;
                _manager = manager;
            }
            manager.SessionsChanged += OnSessionsChanged;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            SyncSessions();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Media: SMTC unavailable", ex);
        }
    }

    private void OnSessionsChanged(GsmtcManager sender, SessionsChangedEventArgs args) => Log.Safe("Media: sessions changed", SyncSessions);

    private void OnCurrentSessionChanged(GsmtcManager sender, CurrentSessionChangedEventArgs args) => Log.Safe("Media: current changed", SyncSessions);

    /// <summary>Adds / removes trackers to match the live SMTC sessions and re-reads the system current session.</summary>
    private void SyncSessions()
    {
        var manager = _manager;
        if (manager is null) return;
        IReadOnlyList<Gsmtc> live;
        string? currentId;
        try
        {
            live = manager.GetSessions();
            currentId = manager.GetCurrentSession()?.SourceAppUserModelId;
        }
        catch (Exception ex)
        {
            Log.Warn("Media: reading sessions failed", ex);
            return;
        }

        var added = new List<Tracker>();
        lock (_gate)
        {
            if (_disposed) return;
            _systemCurrentId = currentId;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in live)
            {
                var id = session.SourceAppUserModelId ?? "";
                if (id.Length == 0 || !seen.Add(id)) continue;
                if (_trackers.TryGetValue(id, out var existing) && ReferenceEquals(existing.Session, session)) continue;
                existing?.Detach();
                var tracker = new Tracker(this, session, id, ResolveAppName(id));
                _trackers[id] = tracker;
                added.Add(tracker);
            }
            foreach (var id in _trackers.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _trackers[id].Detach();
                _trackers.Remove(id);
            }
        }

        foreach (var tracker in added) tracker.Attach();
        Rebuild();
    }

    private string ResolveAppName(string aumid) => _appNames.GetOrAdd(aumid, static id =>
    {
        try
        {
            if (AppNames.PackagedDisplayName(id) is { } packaged) return packaged;
            if (AppNames.ShellAppsFolderName(id) is { } shell) return shell;
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var path in ProcessImage.FindPathsByName(Path.GetFileNameWithoutExtension(id)))
                {
                    if (AppNames.FileDescription(path) is { } description) return description;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Media: app name for {id} failed: {ex.Message}");
        }
        return AppNames.Prettify(id);
    });

    /// <summary>Publishes new snapshots and schedules <see cref="Changed"/>.</summary>
    private void Rebuild()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var list = _trackers.Values.Select(t => OverlaySpotify(t.Snapshot)).ToArray();
            _sessions = list;
            _current =
                (_selectedId is not null ? list.FirstOrDefault(s => s.SessionId == _selectedId) : null)
                ?? (_systemCurrentId is not null ? list.FirstOrDefault(s => s.SessionId == _systemCurrentId) : null)
                ?? list.FirstOrDefault(s => s.IsPlaying)
                ?? list.FirstOrDefault();
        }
        WatchSpotifyTrack();
        _changedNotifier.Trigger();
    }

    /// <inheritdoc />
    public void SelectSession(string? sessionId)
    {
        lock (_gate) _selectedId = sessionId;
        Rebuild();
    }

    /// <inheritdoc />
    public Task PlayPauseAsync() => Command("play/pause", s => s.TryTogglePlayPauseAsync().AsTask());
    /// <inheritdoc />
    public Task PlayAsync() => Command("play", s => s.TryPlayAsync().AsTask());
    /// <inheritdoc />
    public Task PauseAsync() => Command("pause", s => s.TryPauseAsync().AsTask());
    /// <inheritdoc />
    public Task NextAsync() => Command("next", s => s.TrySkipNextAsync().AsTask());
    /// <inheritdoc />
    public Task PreviousAsync() => Command("previous", s => s.TrySkipPreviousAsync().AsTask());

    /// <inheritdoc />
    public async Task SeekAsync(TimeSpan position)
    {
        if (CurrentTracker() is not { } tracker) return;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        var ok = await Command("seek", s => s.TryChangePlaybackPositionAsync((tracker.StartTime + position).Ticks).AsTask(), tracker).ConfigureAwait(false);
        if (ok)
        {
            tracker.ApplyOptimisticPosition(position);
            Rebuild();
        }
    }

    /// <inheritdoc />
    public async Task SetShuffleAsync(bool enabled)
    {
        // Spotify desktop does not report shuffle through SMTC: use the Web API when connected.
        if (UseSpotifyApi(raw => raw.IsShuffleActive is null)) { await SpotifyShuffleAsync(enabled).ConfigureAwait(false); return; }
        var ok = await Command("shuffle", s => s.TryChangeShuffleActiveAsync(enabled).AsTask()).ConfigureAwait(false);
        if (!ok && UseSpotifyApi(_ => true)) await SpotifyShuffleAsync(enabled).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetRepeatAsync(MediaRepeatMode mode)
    {
        if (UseSpotifyApi(raw => raw.RepeatMode is null)) { await SpotifyRepeatAsync(mode).ConfigureAwait(false); return; }
        var ok = await Command("repeat", s => s.TryChangeAutoRepeatModeAsync(mode switch
        {
            MediaRepeatMode.Track => MediaPlaybackAutoRepeatMode.Track,
            MediaRepeatMode.List => MediaPlaybackAutoRepeatMode.List,
            _ => MediaPlaybackAutoRepeatMode.None,
        }).AsTask()).ConfigureAwait(false);
        if (!ok && UseSpotifyApi(_ => true)) await SpotifyRepeatAsync(mode).ConfigureAwait(false);
    }

    private Tracker? CurrentTracker()
    {
        var id = _current?.SessionId;
        if (id is null) return null;
        lock (_gate) return _trackers.GetValueOrDefault(id);
    }

    private async Task<bool> Command(string name, Func<Gsmtc, Task<bool>> action, Tracker? tracker = null)
    {
        tracker ??= CurrentTracker();
        if (tracker is null) return false;
        try
        {
            var ok = await action(tracker.Session).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (!ok) Log.Debug($"Media: {name} rejected by {tracker.Id}");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn($"Media: {name} failed", ex);
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GsmtcManager? manager;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            manager = _manager;
            _manager = null;
            foreach (var tracker in _trackers.Values) tracker.Detach();
            _trackers.Clear();
            _sessions = [];
            _current = null;
        }
        if (manager is not null)
        {
            manager.SessionsChanged -= OnSessionsChanged;
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
        _changedNotifier.Dispose();
        DisposeSpotify();
    }

    /// <summary>One SMTC session: subscribes to its events and keeps an immutable snapshot.</summary>
    private sealed class Tracker(MediaService owner, Gsmtc session, string id, string appName)
    {
        private readonly object _lock = new();
        private int _mediaVersion;
        private bool _detached;
        private MediaSessionInfo _snapshot = new() { SessionId = id, SourceAppId = id, SourceAppName = appName };

        public Gsmtc Session { get; } = session;
        public string Id { get; } = id;
        public TimeSpan StartTime { get; private set; }

        public MediaSessionInfo Snapshot
        {
            get { lock (_lock) return _snapshot; }
        }

        public void Attach()
        {
            Session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            Session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            Session.TimelinePropertiesChanged += OnTimelineChanged;
            ReadPlayback();
            ReadTimeline();
            LoadMediaPropertiesAsync().Forget("Media: properties");
        }

        public void Detach()
        {
            lock (_lock)
            {
                if (_detached) return;
                _detached = true;
            }
            try
            {
                Session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                Session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                Session.TimelinePropertiesChanged -= OnTimelineChanged;
            }
            catch (Exception ex)
            {
                Log.Debug($"Media: detach {Id}: {ex.Message}");
            }
        }

        private void OnMediaPropertiesChanged(Gsmtc sender, MediaPropertiesChangedEventArgs args) =>
            LoadMediaPropertiesAsync().Forget("Media: properties");

        private void OnPlaybackInfoChanged(Gsmtc sender, PlaybackInfoChangedEventArgs args)
        {
            Log.Safe("Media: playback info", ReadPlayback);
            // Some apps only refresh the timeline together with playback state changes.
            Log.Safe("Media: timeline", ReadTimeline);
            owner.Rebuild();
        }

        private void OnTimelineChanged(Gsmtc sender, TimelinePropertiesChangedEventArgs args)
        {
            Log.Safe("Media: timeline", ReadTimeline);
            owner.Rebuild();
        }

        private void Update(Func<MediaSessionInfo, MediaSessionInfo> change)
        {
            lock (_lock)
            {
                if (!_detached) _snapshot = change(_snapshot);
            }
        }

        private void ReadPlayback()
        {
            var info = Session.GetPlaybackInfo();
            if (info is null) return;
            var controls = info.Controls;
            var state = info.PlaybackStatus switch
            {
                GsmtcStatus.Closed => MediaPlaybackState.Closed,
                GsmtcStatus.Changing => MediaPlaybackState.Changing,
                GsmtcStatus.Paused => MediaPlaybackState.Paused,
                GsmtcStatus.Playing => MediaPlaybackState.Playing,
                _ => MediaPlaybackState.Stopped,
            };
            MediaRepeatMode? repeat = info.AutoRepeatMode switch
            {
                MediaPlaybackAutoRepeatMode.None => MediaRepeatMode.None,
                MediaPlaybackAutoRepeatMode.Track => MediaRepeatMode.Track,
                MediaPlaybackAutoRepeatMode.List => MediaRepeatMode.List,
                _ => null,
            };
            var rate = info.PlaybackRate is { } r && r > 0 ? r : 1.0;
            Update(s =>
            {
                // Re-anchor the position on state changes so extrapolation starts from "now".
                var anchored = s.State != state ? s with { Position = s.GetLivePosition(DateTimeOffset.Now), PositionUpdatedAt = DateTimeOffset.Now } : s;
                return anchored with
                {
                    State = state,
                    PlaybackRate = rate,
                    CanPlayPause = controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                    CanNext = controls.IsNextEnabled,
                    CanPrevious = controls.IsPreviousEnabled,
                    CanSeek = controls.IsPlaybackPositionEnabled,
                    IsShuffleActive = controls.IsShuffleEnabled ? info.IsShuffleActive : null,
                    RepeatMode = controls.IsRepeatEnabled ? repeat : null,
                };
            });
        }

        private void ReadTimeline()
        {
            var timeline = Session.GetTimelineProperties();
            if (timeline is null) return;
            var start = timeline.StartTime;
            var duration = timeline.EndTime - start;
            var position = timeline.Position - start;
            var updatedAt = timeline.LastUpdatedTime;
            if (updatedAt.Year < 2000 || updatedAt > DateTimeOffset.Now.AddMinutes(1)) updatedAt = DateTimeOffset.Now;
            StartTime = start;
            Update(s => s with
            {
                Position = position < TimeSpan.Zero ? TimeSpan.Zero : position,
                Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
                PositionUpdatedAt = updatedAt,
            });
        }

        public void ApplyOptimisticPosition(TimeSpan position) =>
            Update(s => s with { Position = position, PositionUpdatedAt = DateTimeOffset.Now });

        private async Task LoadMediaPropertiesAsync()
        {
            var version = Interlocked.Increment(ref _mediaVersion);
            GlobalSystemMediaTransportControlsSessionMediaProperties? props;
            try
            {
                props = await Session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug($"Media: properties of {Id} unavailable: {ex.Message}");
                return;
            }
            if (props is null || version != Volatile.Read(ref _mediaVersion)) return;

            var thumbnail = await ReadThumbnailAsync(props.Thumbnail).ConfigureAwait(false);
            if (version != Volatile.Read(ref _mediaVersion)) return;
            var key = thumbnail is null ? null : Convert.ToHexString(SHA256.HashData(thumbnail).AsSpan(0, 12));

            var artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            Update(s => s with
            {
                Title = props.Title ?? "",
                Artist = artist,
                Album = props.AlbumTitle ?? "",
                Thumbnail = thumbnail,
                ThumbnailKey = key,
            });
            owner.Rebuild();
        }

        private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? reference)
        {
            if (reference is null) return null;
            try
            {
                using var stream = await reference.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (stream.Size == 0 || stream.Size > MaxThumbnailBytes) return null;
                using var reader = new DataReader(stream);
                var loaded = await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false);
                var bytes = new byte[loaded];
                reader.ReadBytes(bytes);
                return bytes.Length > 0 ? bytes : null;
            }
            catch (Exception ex)
            {
                Log.Debug($"Media: thumbnail unavailable: {ex.Message}");
                return null;
            }
        }
    }
}
