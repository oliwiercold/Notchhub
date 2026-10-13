using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;
using NotchHub.Demo.SampleData;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Mutable playback state of one demo media session (queue, play state, timeline). Not thread-safe: the owning
/// <see cref="FakeMediaService"/> calls it under its lock.
/// </summary>
internal sealed class DemoPlayer(DemoMediaSource source, Random random)
{
    private int _index;
    private MediaPlaybackState _state = source.StartsPlaying ? MediaPlaybackState.Playing : MediaPlaybackState.Paused;
    private TimeSpan _position = source.StartPosition;
    private DateTimeOffset _anchor = DateTimeOffset.Now;
    private bool _shuffle;
    private MediaRepeatMode _repeat = MediaRepeatMode.List;

    public string SessionId => source.SessionId;
    public DemoTrack Track => source.Tracks[_index];
    public bool IsPlaying => _state == MediaPlaybackState.Playing;
    public bool HasQueue => source.SupportsQueue && source.Tracks.Count > 1;

    /// <summary>Current position. While live events are paused time stands still (the anchor follows the clock).</summary>
    public TimeSpan Position(DateTimeOffset now)
    {
        if (!DemoServices.LiveEvents) _anchor = now;
        var position = _position;
        if (IsPlaying && now > _anchor) position += now - _anchor;
        return position > Track.Duration ? Track.Duration : position;
    }

    public void SetPlaying(bool playing, DateTimeOffset now)
    {
        Rebase(now);
        _state = playing ? MediaPlaybackState.Playing : MediaPlaybackState.Paused;
    }

    public void Seek(TimeSpan position, DateTimeOffset now)
    {
        _position = position < TimeSpan.Zero ? TimeSpan.Zero : position > Track.Duration ? Track.Duration : position;
        _anchor = now;
    }

    /// <summary>Moves through the queue (wrapping). Shuffle picks a different random track.</summary>
    public bool Skip(int direction, DateTimeOffset now)
    {
        if (!HasQueue) return false;
        var count = source.Tracks.Count;
        _index = _shuffle && direction > 0
            ? (_index + 1 + random.Next(count - 1)) % count
            : ((_index + direction) % count + count) % count;
        Seek(TimeSpan.Zero, now);
        return true;
    }

    public bool SetShuffle(bool enabled)
    {
        if (!HasQueue || _shuffle == enabled) return false;
        _shuffle = enabled;
        return true;
    }

    public bool SetRepeat(MediaRepeatMode mode)
    {
        if (!HasQueue || _repeat == mode) return false;
        _repeat = mode;
        return true;
    }

    /// <summary>Handles the end of the current track (repeat / next / stop). Returns true when the state changed.</summary>
    public bool AdvanceIfEnded(DateTimeOffset now)
    {
        if (!IsPlaying || Position(now) < Track.Duration) return false;
        if (!HasQueue)
        {
            // A single video simply ends.
            Seek(Track.Duration, now);
            _state = MediaPlaybackState.Paused;
            return true;
        }
        switch (_repeat)
        {
            case MediaRepeatMode.Track:
                Seek(TimeSpan.Zero, now);
                break;
            case MediaRepeatMode.None when _index == source.Tracks.Count - 1:
                Skip(1, now);
                _state = MediaPlaybackState.Paused;
                break;
            default:
                Skip(1, now);
                break;
        }
        return true;
    }

    public MediaSessionInfo Snapshot(DateTimeOffset now, IReadOnlyDictionary<AlbumArt, byte[]> art)
    {
        var track = Track;
        var thumbnail = art.GetValueOrDefault(track.Art);
        return new MediaSessionInfo
        {
            SessionId = source.SessionId,
            SourceAppId = source.SessionId,
            SourceAppName = source.SourceAppName,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            Thumbnail = thumbnail,
            ThumbnailKey = thumbnail is null ? null : DemoAssets.ArtKey(track.Art),
            State = _state,
            Position = Position(now),
            PositionUpdatedAt = now,
            Duration = track.Duration,
            PlaybackRate = 1.0,
            CanPlayPause = true,
            CanNext = HasQueue,
            CanPrevious = HasQueue,
            CanSeek = true,
            IsShuffleActive = source.SupportsQueue ? _shuffle : null,
            RepeatMode = source.SupportsQueue ? _repeat : null,
        };
    }

    private void Rebase(DateTimeOffset now)
    {
        _position = Position(now);
        _anchor = now;
    }
}
