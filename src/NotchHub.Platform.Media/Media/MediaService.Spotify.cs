using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Media.Media.Spotify;

namespace NotchHub.Platform.Media.Media;

/// <summary>
/// Optional Spotify Web API layer on top of SMTC: account connection (PKCE), "Liked Songs" state of the playing track and
/// shuffle/repeat for Spotify sessions whose SMTC does not report them. No polling: the like state / player state is fetched
/// when the Spotify track shown as <see cref="Current"/> changes, and on demand (like toggle). Smart Shuffle is not exposed by
/// the public Web API, so it is not offered.
/// </summary>
public sealed partial class MediaService
{
    private readonly object _spGate = new();
    private SpotifyClient? _spotify;
    private volatile SpotifyConnectionState _spState = SpotifyConnectionState.NotConfigured;
    private volatile string? _spError;
    private string? _spClientId;
    private bool _spSettingsHooked;
    // cache for the Spotify track currently shown (keyed by its SMTC title|artist signature)
    private string? _spWatchedSig;
    private string? _spSig;
    private string? _spItemUri;
    private bool? _spLiked;
    private bool? _spShuffle;
    private MediaRepeatMode? _spRepeat;
    private CancellationTokenSource? _spRefreshCts;
    private CancellationTokenSource? _spConnectCts;

    /// <inheritdoc />
    public SpotifyConnectionState SpotifyState => _spState;

    /// <inheritdoc />
    public string? SpotifyAccount => _spState == SpotifyConnectionState.Connected ? _spotify?.Account ?? "Spotify" : null;

    /// <inheritdoc />
    public string? SpotifyError => _spError;

    /// <inheritdoc />
    public bool? IsCurrentTrackLiked
    {
        get
        {
            var cur = _current;
            if (cur is null || !IsSpotify(cur) || _spState != SpotifyConnectionState.Connected) return null;
            var sig = Sig(cur);
            lock (_spGate) return _spSig == sig ? _spLiked : null;
        }
    }

    private static bool IsSpotify(MediaSessionInfo s) =>
        s.SourceAppId.Contains("spotify", StringComparison.OrdinalIgnoreCase) || s.SourceAppName.Equals("Spotify", StringComparison.OrdinalIgnoreCase);

    private static string Sig(MediaSessionInfo s) => s.Title + "\u001f" + s.Artist;

    // ───────────── configuration ─────────────

    private void InitSpotify()
    {
        _spotify ??= new SpotifyClient(Path.Combine(AppPaths.Dir("media"), "spotify-session.bin"));
        if (AppServices.Settings is { } settings && !_spSettingsHooked)
        {
            settings.Changed += OnSettingsChanged;
            _spSettingsHooked = true;
        }
        ApplyClientId(ReadClientId());
    }

    private static string? ReadClientId()
    {
        var id = AppServices.Settings?.Current?.Media?.SpotifyClientId?.Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Log.Safe("Media: Spotify settings", () => ApplyClientId(ReadClientId()));

    private void ApplyClientId(string? clientId)
    {
        if (_spotify is not { } client) return;
        lock (_spGate)
        {
            if (clientId == _spClientId) return;
            _spClientId = clientId;
        }
        _spConnectCts?.Cancel();
        ResetSpotifyCache();
        if (clientId is null)
        {
            SetSpotifyState(SpotifyConnectionState.NotConfigured, null);
            return;
        }
        client.Load(clientId);
        SetSpotifyState(client.HasSession ? SpotifyConnectionState.Connected : SpotifyConnectionState.Disconnected, null);
        if (client.HasSession) ValidateSessionAsync(client).Forget("Media: Spotify validate");
    }

    /// <summary>Refreshes the account name at start; a revoked session turns into Disconnected (network errors are ignored).</summary>
    private async Task ValidateSessionAsync(SpotifyClient client)
    {
        try
        {
            await client.GetAccountAsync(CancellationToken.None).ConfigureAwait(false);
            Rebuild();
        }
        catch (SpotifyException ex) { HandleSpotifyFailure(ex, "validate"); }
    }

    private void SetSpotifyState(SpotifyConnectionState state, string? error)
    {
        _spState = state;
        _spError = error;
        Rebuild();
    }

    private void ResetSpotifyCache()
    {
        lock (_spGate)
        {
            _spWatchedSig = _spSig = _spItemUri = null;
            _spLiked = null;
            _spShuffle = null;
            _spRepeat = null;
            _spRefreshCts?.Cancel();
            _spRefreshCts = null;
        }
    }

    /// <summary>Lost sessions → Disconnected; account problems are surfaced; transient errors are only logged.</summary>
    private void HandleSpotifyFailure(SpotifyException ex, string context)
    {
        Log.Debug($"Media: Spotify {context} failed: {ex.Code}");
        if (ex.SessionLost)
        {
            ResetSpotifyCache();
            SetSpotifyState(_spClientId is null ? SpotifyConnectionState.NotConfigured : SpotifyConnectionState.Disconnected, ex.Code);
        }
        else if (ex.Code is "forbidden" or "premium_required" or "unauthorized" or "invalid_client")
        {
            _spError = ex.Code;
            Rebuild();
        }
    }

    // ───────────── connect / disconnect ─────────────

    /// <inheritdoc />
    public async Task<bool> ConnectSpotifyAsync(CancellationToken ct = default)
    {
        if (_spotify is null) Log.Safe("Media: Spotify init", InitSpotify);
        var client = _spotify;
        var clientId = ReadClientId();
        if (client is null) return false;
        if (clientId is null)
        {
            SetSpotifyState(SpotifyConnectionState.NotConfigured, "not_configured");
            return false;
        }
        if (_spState == SpotifyConnectionState.Connecting) return false;
        lock (_spGate) _spClientId = clientId;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Interlocked.Exchange(ref _spConnectCts, cts)?.Cancel();
        SetSpotifyState(SpotifyConnectionState.Connecting, null);
        try
        {
            await client.SignInAsync(clientId, cts.Token).ConfigureAwait(false);
            ResetSpotifyCache();
            SetSpotifyState(SpotifyConnectionState.Connected, null);
            return true;
        }
        catch (SpotifyException ex)
        {
            Log.Warn($"Media: Spotify sign-in failed: {ex.Code}");
            SetSpotifyState(SpotifyConnectionState.Error, ex.Code);
            return false;
        }
        catch (OperationCanceledException)
        {
            SetSpotifyState(_spotify?.HasSession == true ? SpotifyConnectionState.Connected : SpotifyConnectionState.Disconnected, "cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("Media: Spotify sign-in failed", ex);
            SetSpotifyState(SpotifyConnectionState.Error, "unknown");
            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref _spConnectCts, null, cts);
            cts.Dispose();
        }
    }

    /// <inheritdoc />
    public Task DisconnectSpotifyAsync()
    {
        try { _spConnectCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        _spotify?.SignOut();
        ResetSpotifyCache();
        SetSpotifyState(ReadClientId() is null ? SpotifyConnectionState.NotConfigured : SpotifyConnectionState.Disconnected, null);
        Log.Info("Spotify: signed out");
        return Task.CompletedTask;
    }

    // ───────────── track state ─────────────

    /// <summary>Fills shuffle / repeat of Spotify sessions from the Web API when SMTC does not report them.</summary>
    private MediaSessionInfo OverlaySpotify(MediaSessionInfo s)
    {
        if (_spState != SpotifyConnectionState.Connected || !IsSpotify(s) || (s.IsShuffleActive is not null && s.RepeatMode is not null)) return s;
        bool? shuffle;
        MediaRepeatMode? repeat;
        lock (_spGate)
        {
            shuffle = _spShuffle;
            repeat = _spRepeat;
        }
        if (shuffle is null && repeat is null) return s;
        return s with { IsShuffleActive = s.IsShuffleActive ?? shuffle, RepeatMode = s.RepeatMode ?? repeat };
    }

    /// <summary>Called after every rebuild: schedules one Web API refresh when the shown Spotify track changes.</summary>
    private void WatchSpotifyTrack()
    {
        if (_spState != SpotifyConnectionState.Connected) return;
        var cur = _current;
        if (cur is null || !IsSpotify(cur) || cur.Title.Length == 0) return;
        var sig = Sig(cur);
        CancellationTokenSource cts;
        lock (_spGate)
        {
            if (sig == _spWatchedSig) return;
            _spWatchedSig = sig;
            _spRefreshCts?.Cancel();
            cts = _spRefreshCts = new CancellationTokenSource();
        }
        // SMTC usually announces the new track before the Web API reflects it — wait a moment.
        RefreshSpotifyTrackAsync(sig, cur.Title, TimeSpan.FromMilliseconds(900), cts.Token).Forget("Media: Spotify refresh");
    }

    private async Task<bool> RefreshSpotifyTrackAsync(string sig, string title, TimeSpan delay, CancellationToken ct)
    {
        if (_spotify is not { } client) return false;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);
            SpotifyPlayback? playback = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                playback = await client.GetPlaybackAsync(ct).ConfigureAwait(false);
                if (playback is null || TitleMatches(playback.ItemName, title)) break;
                await Task.Delay(TimeSpan.FromMilliseconds(1200 * (attempt + 1)), ct).ConfigureAwait(false);
            }
            // never attach a URI of a different track (local files, another device…) to the shown one
            var uri = playback is not null && TitleMatches(playback.ItemName, title) ? playback.ItemUri : null;
            bool? liked = uri is null ? null : await client.IsInLibraryAsync(uri, ct).ConfigureAwait(false);
            lock (_spGate)
            {
                if (playback is not null)
                {
                    _spShuffle = playback.Shuffle;
                    _spRepeat = playback.Repeat switch { "track" => MediaRepeatMode.Track, "context" => MediaRepeatMode.List, "off" => MediaRepeatMode.None, _ => null };
                }
                if (_spWatchedSig != sig) return false;
                _spSig = sig;
                _spItemUri = uri;
                _spLiked = liked;
            }
            Rebuild();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (SpotifyException ex)
        {
            HandleSpotifyFailure(ex, "refresh");
            return false;
        }
    }

    private static bool TitleMatches(string apiName, string smtcTitle)
    {
        if (apiName.Length == 0 || smtcTitle.Length == 0) return false;
        return apiName.Equals(smtcTitle, StringComparison.OrdinalIgnoreCase)
            || apiName.Contains(smtcTitle, StringComparison.OrdinalIgnoreCase)
            || smtcTitle.Contains(apiName, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<bool> SetCurrentTrackLikedAsync(bool liked)
    {
        var cur = _current;
        if (_spotify is not { } client || cur is null || !IsSpotify(cur) || _spState != SpotifyConnectionState.Connected) return false;
        var sig = Sig(cur);
        string? uri;
        lock (_spGate) uri = _spSig == sig ? _spItemUri : null;
        if (uri is null)
        {
            CancellationToken token;
            lock (_spGate)
            {
                _spWatchedSig = sig;
                _spRefreshCts?.Cancel();
                token = (_spRefreshCts = new CancellationTokenSource()).Token;
            }
            await RefreshSpotifyTrackAsync(sig, cur.Title, TimeSpan.Zero, token).ConfigureAwait(false);
            lock (_spGate) uri = _spSig == sig ? _spItemUri : null;
            if (uri is null) return false;
        }

        bool? previous;
        lock (_spGate)
        {
            previous = _spLiked;
            if (_spSig == sig) _spLiked = liked; // optimistic
        }
        Rebuild();
        try
        {
            await client.SetInLibraryAsync(uri, liked, CancellationToken.None).ConfigureAwait(false);
            _spError = null;
            return true;
        }
        catch (SpotifyException ex)
        {
            lock (_spGate) if (_spSig == sig) _spLiked = previous;
            HandleSpotifyFailure(ex, "library");
            Rebuild();
            return false;
        }
    }

    // ───────────── shuffle / repeat fallback ─────────────

    /// <summary>True when the current session is Spotify, the account is connected and <paramref name="rawCheck"/> holds for its raw SMTC snapshot.</summary>
    private bool UseSpotifyApi(Func<MediaSessionInfo, bool> rawCheck)
    {
        if (_spState != SpotifyConnectionState.Connected) return false;
        var cur = _current;
        if (cur is null || !IsSpotify(cur)) return false;
        return CurrentTracker() is { } tracker && rawCheck(tracker.Snapshot);
    }

    private async Task SpotifyShuffleAsync(bool enabled)
    {
        if (_spotify is not { } client) return;
        try
        {
            await client.SetShuffleAsync(enabled, CancellationToken.None).ConfigureAwait(false);
            lock (_spGate) _spShuffle = enabled;
        }
        catch (SpotifyException ex) { HandleSpotifyFailure(ex, "shuffle"); }
        Rebuild();
    }

    private async Task SpotifyRepeatAsync(MediaRepeatMode mode)
    {
        if (_spotify is not { } client) return;
        try
        {
            await client.SetRepeatAsync(mode switch { MediaRepeatMode.Track => "track", MediaRepeatMode.List => "context", _ => "off" }, CancellationToken.None).ConfigureAwait(false);
            lock (_spGate) _spRepeat = mode;
        }
        catch (SpotifyException ex) { HandleSpotifyFailure(ex, "repeat"); }
        Rebuild();
    }

    private void DisposeSpotify()
    {
        if (_spSettingsHooked && AppServices.Settings is { } settings) settings.Changed -= OnSettingsChanged;
        try { _spConnectCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        lock (_spGate) _spRefreshCts?.Cancel();
        _spotify?.Dispose();
    }
}
