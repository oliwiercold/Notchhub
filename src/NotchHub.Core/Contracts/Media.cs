namespace NotchHub.Core.Contracts;

public enum MediaPlaybackState { Closed, Stopped, Changing, Paused, Playing }

public enum MediaRepeatMode { None, Track, List }

/// <summary>Snapshot of one Windows media session (SMTC: Spotify, browsers, etc.).</summary>
public sealed record MediaSessionInfo
{
    /// <summary>Stable id of the session (source app user model id).</summary>
    public required string SessionId { get; init; }
    public required string SourceAppId { get; init; }
    /// <summary>Friendly app name, e.g. "Spotify", "Google Chrome".</summary>
    public string SourceAppName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    /// <summary>Encoded thumbnail (PNG/JPEG bytes) or null.</summary>
    public byte[]? Thumbnail { get; init; }
    /// <summary>Changes whenever the thumbnail changes (e.g. a hash) so UIs can cache decoded images.</summary>
    public string? ThumbnailKey { get; init; }
    public MediaPlaybackState State { get; init; }
    public TimeSpan Position { get; init; }
    /// <summary>Zero when unknown (live streams).</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Wall clock time at which <see cref="Position"/> was valid.</summary>
    public DateTimeOffset PositionUpdatedAt { get; init; }
    public double PlaybackRate { get; init; } = 1.0;
    public bool CanPlayPause { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanSeek { get; init; }
    public bool? IsShuffleActive { get; init; }
    public MediaRepeatMode? RepeatMode { get; init; }

    public bool IsPlaying => State == MediaPlaybackState.Playing;

    /// <summary>Position extrapolated to <paramref name="now"/> (clamped to the duration).</summary>
    public TimeSpan GetLivePosition(DateTimeOffset now)
    {
        var pos = Position;
        if (State == MediaPlaybackState.Playing)
        {
            var elapsed = now - PositionUpdatedAt;
            if (elapsed > TimeSpan.Zero) pos += TimeSpan.FromTicks((long)(elapsed.Ticks * (PlaybackRate <= 0 ? 1 : PlaybackRate)));
        }
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
        return pos;
    }
}

public enum SpotifyConnectionState { NotConfigured, Disconnected, Connecting, Connected, Error }

public interface IMediaService : IBackgroundService
{
    /// <summary>The session the UI shows: the user-selected one if still alive, else the system "current" session, else null.</summary>
    MediaSessionInfo? Current { get; }

    /// <summary>All live sessions.</summary>
    IReadOnlyList<MediaSessionInfo> Sessions { get; }

    /// <summary>Raised on any change: sessions added/removed, metadata, thumbnail, playback state, timeline.</summary>
    event EventHandler? Changed;

    Task PlayPauseAsync();
    Task PlayAsync();
    Task PauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);
    Task SetShuffleAsync(bool enabled);
    Task SetRepeatAsync(MediaRepeatMode mode);

    /// <summary>Makes <paramref name="sessionId"/> the <see cref="Current"/> session; null = follow the system current session.</summary>
    void SelectSession(string? sessionId);

    // ── Spotify Web API (optional; OAuth PKCE with the user's own client id from settings.Media.SpotifyClientId) ──

    SpotifyConnectionState SpotifyState { get; }
    /// <summary>Display name of the connected Spotify account.</summary>
    string? SpotifyAccount { get; }
    /// <summary>Short error code of the last Spotify failure (UI localizes).</summary>
    string? SpotifyError { get; }
    /// <summary>Whether the track playing on Spotify is in the user's library; null when unknown / not Spotify / not connected.</summary>
    bool? IsCurrentTrackLiked { get; }
    /// <summary>Opens the browser for Spotify sign-in (PKCE + loopback redirect). True on success.</summary>
    Task<bool> ConnectSpotifyAsync(CancellationToken ct = default);
    Task DisconnectSpotifyAsync();
    /// <summary>Adds / removes the currently playing Spotify track to / from "Liked Songs".</summary>
    Task<bool> SetCurrentTrackLikedAsync(bool liked);
}
