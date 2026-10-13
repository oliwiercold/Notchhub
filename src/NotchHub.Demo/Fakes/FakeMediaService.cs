using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;
using NotchHub.Demo.Infrastructure;
using NotchHub.Demo.SampleData;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo media sessions: Spotify playing "lofi beats to study and relax" (3-track queue with generated covers)
/// and a paused YouTube video in Chrome. Transport controls, seek, shuffle/repeat and session selection work in memory.
/// </summary>
public sealed class FakeMediaService : FakeBackgroundService, IMediaService
{
    private readonly DemoPlayer[] _players;
    private readonly Dictionary<AlbumArt, byte[]> _art = [];
    private string? _selectedId;
    private string _systemCurrentId;

    /// <summary>Creates the sessions in their initial (screenshot) state.</summary>
    public FakeMediaService()
    {
        var random = DemoRandom.For("media");
        _players = [new DemoPlayer(MediaCatalog.Spotify, random), new DemoPlayer(MediaCatalog.YouTube, random)];
        _systemCurrentId = MediaCatalog.Spotify.SessionId;
        // the first Spotify track starts "liked" so screenshots show the filled heart
        _liked.Add(_players[0].Snapshot(DateTimeOffset.Now, _art).Title);
    }

    // ───────────── Spotify (demo: connected as "Demo", likes kept in memory) ─────────────

    private readonly HashSet<string> _liked = new(StringComparer.Ordinal);
    private SpotifyConnectionState _spotifyState = SpotifyConnectionState.Connected;

    /// <inheritdoc />
    public SpotifyConnectionState SpotifyState
    {
        get { lock (Gate) return _spotifyState; }
    }

    /// <inheritdoc />
    public string? SpotifyAccount => SpotifyState == SpotifyConnectionState.Connected ? "Demo" : null;

    /// <inheritdoc />
    public string? SpotifyError => null;

    /// <inheritdoc />
    public bool? IsCurrentTrackLiked
    {
        get
        {
            lock (Gate)
            {
                if (_spotifyState != SpotifyConnectionState.Connected) return null;
                var player = CurrentPlayer();
                if (player is null || player.SessionId != MediaCatalog.Spotify.SessionId) return null;
                return _liked.Contains(player.Snapshot(DateTimeOffset.Now, _art).Title);
            }
        }
    }

    /// <inheritdoc />
    public Task<bool> ConnectSpotifyAsync(CancellationToken ct = default)
    {
        lock (Gate) _spotifyState = SpotifyConnectionState.Connected;
        Raise(Changed);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DisconnectSpotifyAsync()
    {
        lock (Gate) _spotifyState = SpotifyConnectionState.Disconnected;
        Raise(Changed);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> SetCurrentTrackLikedAsync(bool liked)
    {
        lock (Gate)
        {
            var player = CurrentPlayer();
            if (_spotifyState != SpotifyConnectionState.Connected || player is null || player.SessionId != MediaCatalog.Spotify.SessionId)
                return Task.FromResult(false);
            var title = player.Snapshot(DateTimeOffset.Now, _art).Title;
            if (liked) _liked.Add(title); else _liked.Remove(title);
        }
        Raise(Changed);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public MediaSessionInfo? Current
    {
        get
        {
            lock (Gate) return CurrentPlayer()?.Snapshot(DateTimeOffset.Now, _art);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MediaSessionInfo> Sessions
    {
        get
        {
            var now = DateTimeOffset.Now;
            lock (Gate) return _players.Select(p => p.Snapshot(now, _art)).ToArray();
        }
    }

    /// <inheritdoc />
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        var arts = Enum.GetValues<AlbumArt>();
        var images = await Task.WhenAll(arts.Select(DemoAssets.AlbumArtAsync)).ConfigureAwait(false);
        lock (Gate)
        {
            for (var i = 0; i < arts.Length; i++)
                if (images[i] is { } png) _art[arts[i]] = png;
        }
        Raise(Changed);

        StartLiveLoop("media", () => TimeSpan.FromSeconds(1), () =>
        {
            bool changed;
            lock (Gate)
            {
                var now = DateTimeOffset.Now;
                changed = false;
                foreach (var player in _players) changed |= player.AdvanceIfEnded(now);
            }
            if (changed) Raise(Changed);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task PlayPauseAsync() => Mutate(p =>
    {
        p.SetPlaying(!p.IsPlaying, DateTimeOffset.Now);
        return true;
    });

    /// <inheritdoc />
    public Task PlayAsync() => Mutate(p =>
    {
        if (p.IsPlaying) return false;
        p.SetPlaying(true, DateTimeOffset.Now);
        return true;
    });

    /// <inheritdoc />
    public Task PauseAsync() => Mutate(p =>
    {
        if (!p.IsPlaying) return false;
        p.SetPlaying(false, DateTimeOffset.Now);
        return true;
    });

    /// <inheritdoc />
    public Task NextAsync() => Mutate(p => p.Skip(1, DateTimeOffset.Now));

    /// <inheritdoc />
    public Task PreviousAsync() => Mutate(p => p.Skip(-1, DateTimeOffset.Now));

    /// <inheritdoc />
    public Task SeekAsync(TimeSpan position) => Mutate(p =>
    {
        p.Seek(position, DateTimeOffset.Now);
        return true;
    });

    /// <inheritdoc />
    public Task SetShuffleAsync(bool enabled) => Mutate(p => p.SetShuffle(enabled));

    /// <inheritdoc />
    public Task SetRepeatAsync(MediaRepeatMode mode) => Mutate(p => p.SetRepeat(mode));

    /// <inheritdoc />
    public void SelectSession(string? sessionId)
    {
        lock (Gate)
        {
            if (sessionId is not null && _players.All(p => p.SessionId != sessionId)) return;
            if (_selectedId == sessionId) return;
            _selectedId = sessionId;
        }
        Raise(Changed);
    }

    private DemoPlayer? CurrentPlayer() =>
        _players.FirstOrDefault(p => p.SessionId == _selectedId) ?? _players.FirstOrDefault(p => p.SessionId == _systemCurrentId);

    /// <summary>Applies <paramref name="change"/> to the current session; raises Changed when it reports a change.</summary>
    private Task Mutate(Func<DemoPlayer, bool> change)
    {
        bool changed;
        lock (Gate)
        {
            var player = CurrentPlayer();
            if (player is null) return Task.CompletedTask;
            changed = change(player);
            // Like SMTC: the session that most recently started playing becomes the system "current" session.
            if (changed && player.IsPlaying) _systemCurrentId = player.SessionId;
        }
        if (changed) Raise(Changed);
        return Task.CompletedTask;
    }
}
