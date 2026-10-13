using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NotchHub.Core;
using NotchHub.Platform.Media.Common;

namespace NotchHub.Platform.Media.Media.Spotify;

/// <summary>A Spotify failure with a short code the UI localizes (never contains tokens).</summary>
internal sealed class SpotifyException(string code, bool sessionLost = false, Exception? inner = null) : Exception(code, inner)
{
    public string Code { get; } = code;
    /// <summary>The stored refresh token is no longer valid; the user must sign in again.</summary>
    public bool SessionLost { get; } = sessionLost;
}

/// <summary>
/// Minimal Spotify Web API client: PKCE sign-in through a one-shot loopback listener (127.0.0.1:<see cref="SpotifyAuth.RedirectPort"/>),
/// access-token refresh, refresh token persisted with DPAPI, and the handful of endpoints NotchHub uses:
/// GET /me, GET /me/player, GET/PUT/DELETE /me/library(/contains) (the 2026 replacement of /me/tracks), PUT /me/player/shuffle|repeat.
/// Smart Shuffle is NOT available in the public Web API (GET /me/player may report <c>smart_shuffle</c>, but nothing can set it).
/// Tokens are never logged.
/// </summary>
internal sealed class SpotifyClient : IDisposable
{
    private const string ApiBase = "https://api.spotify.com/v1";
    private static readonly byte[] Entropy = "NotchHub.Spotify.v1"u8.ToArray();
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private static readonly HttpClient Http = CreateHttp();

    private readonly string _storePath;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly object _lock = new();
    /// <summary>Serializes the session file write/delete so a late Persist cannot resurrect a deleted session.</summary>
    private readonly object _fileLock = new();
    /// <summary>Bumped by Load, SignOut and a completed sign-in; a refresh started under an older generation is discarded.</summary>
    private int _sessionGen;
    private string? _clientId;
    private string? _refreshToken;
    private string? _accessToken;
    private DateTimeOffset _accessExpires;

    public SpotifyClient(string storePath) => _storePath = storePath;

    /// <summary>Display name of the signed-in account (cached in the store).</summary>
    public string? Account { get; private set; }

    public bool HasSession
    {
        get { lock (_lock) return _refreshToken is not null && _clientId is not null; }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NotchHub/1.0");
        return http;
    }

    // ───────────── persistence ─────────────

    /// <summary>Loads the stored session if it belongs to <paramref name="clientId"/>; otherwise the client has no session.</summary>
    public void Load(string clientId)
    {
        int gen;
        lock (_lock)
        {
            gen = ++_sessionGen;
            _clientId = clientId;
            _refreshToken = null;
            _accessToken = null;
            Account = null;
        }
        try
        {
            if (!File.Exists(_storePath)) return;
            var json = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(_storePath), Entropy));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var storedClient = root.TryGetProperty("clientId", out var c) ? c.GetString() : null;
            var refresh = root.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;
            var account = root.TryGetProperty("account", out var a) ? a.GetString() : null;
            if (!string.Equals(storedClient, clientId, StringComparison.Ordinal) || string.IsNullOrEmpty(refresh)) return;
            lock (_lock)
            {
                if (gen != _sessionGen) return; // signed out / signed in again meanwhile
                _refreshToken = refresh;
                Account = account;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Spotify: stored session unreadable ({ex.GetType().Name})");
        }
    }

    private void Persist()
    {
        // Held across read + write: SignOut clears memory first and then waits here, so it always deletes after any write.
        lock (_fileLock)
        {
            string? clientId, refresh, account;
            lock (_lock)
            {
                clientId = _clientId;
                refresh = _refreshToken;
                account = Account;
            }
            if (clientId is null || refresh is null) return;
            try
            {
                var json = JsonSerializer.Serialize(new Dictionary<string, string?> { ["clientId"] = clientId, ["refreshToken"] = refresh, ["account"] = account });
                var tmp = _storePath + ".tmp";
                File.WriteAllBytes(tmp, Dpapi.Protect(Encoding.UTF8.GetBytes(json), Entropy));
                File.Move(tmp, _storePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warn($"Spotify: saving the session failed ({ex.GetType().Name})");
            }
        }
    }

    /// <summary>Forgets the session (memory + file).</summary>
    public void SignOut()
    {
        lock (_lock)
        {
            _sessionGen++;
            _refreshToken = null;
            _accessToken = null;
            Account = null;
        }
        lock (_fileLock)
        {
            try { if (File.Exists(_storePath)) File.Delete(_storePath); }
            catch (Exception ex) { Log.Warn($"Spotify: deleting the session failed ({ex.GetType().Name})"); }
        }
    }

    // ───────────── sign-in (PKCE + loopback) ─────────────

    /// <summary>Opens the browser, waits for the redirect, exchanges the code and reads the account name.</summary>
    public async Task<string> SignInAsync(string clientId, CancellationToken ct)
    {
        var verifier = SpotifyAuth.CreateVerifier();
        var state = SpotifyAuth.CreateState();
        var listener = new TcpListener(IPAddress.Loopback, SpotifyAuth.RedirectPort);
        try { listener.Start(); }
        catch (SocketException ex) { throw new SpotifyException("port_in_use", inner: ex); }

        SpotifyAuth.Callback callback;
        try
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(SpotifyAuth.AuthorizeUrl(clientId, SpotifyAuth.Challenge(verifier), state)) { UseShellExecute = true })) { }
            }
            catch (Exception ex) { throw new SpotifyException("browser", inner: ex); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SignInTimeout);
            try { callback = await WaitForCallbackAsync(listener, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SpotifyException("timeout"); }
        }
        finally { listener.Stop(); }

        if (callback.Error is { } error) throw new SpotifyException(error == "access_denied" ? "denied" : "auth_" + Sanitize(error));
        if (!string.Equals(callback.State, state, StringComparison.Ordinal)) throw new SpotifyException("state_mismatch");
        if (string.IsNullOrEmpty(callback.Code)) throw new SpotifyException("no_code");

        var token = await PostTokenAsync(SpotifyAuth.CodeExchangeForm(clientId, callback.Code, verifier), ct).ConfigureAwait(false);
        if (token.RefreshToken is null) throw new SpotifyException("no_refresh_token");
        lock (_lock)
        {
            _sessionGen++;
            _clientId = clientId;
            _refreshToken = token.RefreshToken;
            _accessToken = token.AccessToken;
            _accessExpires = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            Account = null;
        }
        string? account;
        try { account = await GetAccountAsync(ct).ConfigureAwait(false); }
        catch (SpotifyException ex) when (ex.Code == "forbidden")
        {
            // Development-mode apps answer 403 for accounts not added under "User Management".
            SignOut();
            throw new SpotifyException("user_not_allowed");
        }
        catch
        {
            SignOut();
            throw;
        }
        Persist();
        Log.Info("Spotify: signed in");
        return account ?? "Spotify";
    }

    /// <summary>
    /// Accepts connections until one carries a valid /callback request. Each connection is handled on its own task, so an idle
    /// (browser preconnect) or reset socket can neither block nor abort the real redirect queued behind it.
    /// </summary>
    internal static async Task<SpotifyAuth.Callback> WaitForCallbackAsync(TcpListener listener, CancellationToken ct)
    {
        var found = new TaskCompletionSource<SpotifyAuth.Callback>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlers = new List<Task>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<TcpClient>? accept = null;
        try
        {
            while (true)
            {
                accept = listener.AcceptTcpClientAsync(stop.Token).AsTask();
                if (await Task.WhenAny(accept, found.Task).ConfigureAwait(false) == found.Task) return await found.Task.ConfigureAwait(false);
                TcpClient client;
                try { client = await accept.ConfigureAwait(false); }
                catch (SocketException) { continue; } // connection aborted before the accept completed
                accept = null;
                handlers.RemoveAll(t => t.IsCompleted);
                handlers.Add(HandleConnectionAsync(client, found, stop.Token));
            }
        }
        finally
        {
            stop.Cancel();
            // A pending accept is cancelled by stop; one that completed in the same instant as the callback is closed here.
            if (accept is not null) _ = accept.ContinueWith(static t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); }, TaskScheduler.Default);
            try { await Task.WhenAll(handlers).ConfigureAwait(false); } catch { /* handlers swallow their own errors */ }
        }
    }

    private static async Task HandleConnectionAsync(TcpClient client, TaskCompletionSource<SpotifyAuth.Callback> found, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var line = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
                if (line is null) return; // idle preconnect, reset, or closed without a request
                var target = SpotifyAuth.RequestTarget(line);
                var callback = target is null ? null : SpotifyAuth.ParseCallback(target);
                try { await RespondAsync(stream, callback is not null, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
                if (callback is not null) found.TrySetResult(callback);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // a broken connection is ignored; the listener keeps waiting for the real redirect
        }
    }

    /// <summary>Reads up to the first CRLF. Null when the peer closes, resets or sends nothing for 10 s.</summary>
    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        int total = 0;
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (total < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(total), readTimeout.Token).ConfigureAwait(false);
                if (n == 0) break;
                total += n;
                var text = Encoding.ASCII.GetString(buffer, 0, total);
                var end = text.IndexOf("\r\n", StringComparison.Ordinal);
                if (end >= 0) return text[..end]; // the request line is all we need
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (IOException) { return null; }
        return null;
    }

    private static async Task RespondAsync(NetworkStream stream, bool ok, CancellationToken ct)
    {
        // Not app UI: a neutral bilingual page the user sees for a second in the browser.
        var body = ok
            ? "<!doctype html><meta charset=utf-8><title>NotchHub</title><body style=\"font-family:Segoe UI,sans-serif;background:#121212;color:#eee;display:grid;place-items:center;height:90vh\">"
              + "<div style=\"text-align:center\"><h2>NotchHub &#10003; Spotify</h2><p>Możesz zamknąć tę kartę. &middot; You can close this tab.</p></div>"
            : "<!doctype html><meta charset=utf-8><title>NotchHub</title><p>Not found</p>";
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {(ok ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (IOException) { }
    }

    private static string Sanitize(string s) => new(s.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_').Take(40).ToArray());

    // ───────────── tokens ─────────────

    private static async Task<SpotifyToken> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SpotifyAuth.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
            response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new SpotifyException("network", inner: ex); }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try { return SpotifyJson.ParseToken(body); }
                catch (Exception ex) when (ex is JsonException or FormatException) { throw new SpotifyException("token_failed", inner: ex); }
            }
            var error = SpotifyJson.ParseOAuthError(body);
            Log.Warn($"Spotify: token endpoint answered {(int)response.StatusCode} ({error ?? "?"})");
            throw error switch
            {
                "invalid_grant" => new SpotifyException("session_expired", sessionLost: true),
                "invalid_client" or "unauthorized_client" => new SpotifyException("invalid_client", sessionLost: form["grant_type"] == "refresh_token"),
                _ when (int)response.StatusCode == 429 => new SpotifyException("rate_limited"),
                _ => new SpotifyException("token_failed"),
            };
        }
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? clientId, refresh;
            int gen;
            lock (_lock)
            {
                if (!forceRefresh && _accessToken is { } token && DateTimeOffset.UtcNow < _accessExpires - TimeSpan.FromSeconds(60)) return token;
                clientId = _clientId;
                refresh = _refreshToken;
                gen = _sessionGen;
            }
            if (clientId is null || refresh is null) throw new SpotifyException("not_connected", sessionLost: true);
            SpotifyToken fresh;
            try { fresh = await PostTokenAsync(SpotifyAuth.RefreshForm(clientId, refresh), ct).ConfigureAwait(false); }
            catch (SpotifyException ex) when (ex.SessionLost)
            {
                SignOutIfCurrent(gen); // a stale failure must not sign out a session created meanwhile
                throw;
            }
            if (!TryApplyRefresh(gen, fresh, out bool rotated)) throw new SpotifyException("not_connected", sessionLost: true);
            if (rotated) Persist();
            return fresh.AccessToken;
        }
        finally { _tokenLock.Release(); }
    }

    /// <summary>
    /// Applies a refresh result only if the session it was requested for is still current (no SignOut / Load / new sign-in
    /// happened during the request). Otherwise the result is discarded so a disconnected session is not brought back and a
    /// newer account's rotated refresh token is not overwritten.
    /// </summary>
    internal bool TryApplyRefresh(int gen, SpotifyToken fresh, out bool rotated)
    {
        lock (_lock)
        {
            rotated = false;
            if (gen != _sessionGen || _refreshToken is null) return false;
            _accessToken = fresh.AccessToken;
            _accessExpires = DateTimeOffset.UtcNow.AddSeconds(fresh.ExpiresIn);
            rotated = fresh.RefreshToken is not null && fresh.RefreshToken != _refreshToken;
            if (rotated) _refreshToken = fresh.RefreshToken;
            return true;
        }
    }

    internal int SessionGeneration
    {
        get { lock (_lock) return _sessionGen; }
    }

    private void SignOutIfCurrent(int gen)
    {
        bool current;
        lock (_lock) current = gen == _sessionGen;
        if (current) SignOut();
    }

    // ───────────── Web API ─────────────

    /// <summary>Sends an API request (refreshing once on 401). Returns the body ("" for 204).</summary>
    private async Task<string> SendAsync(HttpMethod method, string pathAndQuery, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 0, ct).ConfigureAwait(false);
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(method, ApiBase + pathAndQuery);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (method == HttpMethod.Put || method == HttpMethod.Delete) request.Content = new ByteArrayContent([]);
                response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new SpotifyException("network", inner: ex); }

            using (response)
            {
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return body;
                if (status == 401 && attempt == 0) continue;
                var reason = SpotifyJson.ParseApiReason(body);
                Log.Debug($"Spotify: {method} {StripQuery(pathAndQuery)} → {status} {reason}");
                throw status switch
                {
                    401 => new SpotifyException("unauthorized"),
                    403 when reason == "PREMIUM_REQUIRED" => new SpotifyException("premium_required"),
                    403 => new SpotifyException("forbidden"),
                    404 when reason == "NO_ACTIVE_DEVICE" => new SpotifyException("no_device"),
                    429 => new SpotifyException("rate_limited"),
                    _ => new SpotifyException("http_" + status),
                };
            }
        }
    }

    private static string StripQuery(string path) => path.IndexOf('?') is var q and >= 0 ? path[..q] : path;

    /// <summary>GET /me → display name; also refreshes the cached account.</summary>
    public async Task<string?> GetAccountAsync(CancellationToken ct)
    {
        var body = await SendAsync(HttpMethod.Get, "/me", ct).ConfigureAwait(false);
        string? name;
        try { name = SpotifyJson.ParseProfileName(body); }
        catch (JsonException) { name = null; }
        bool changed;
        lock (_lock)
        {
            if (_refreshToken is null) name = null; // signed out while the request was in flight
            changed = name is not null && name != Account;
            if (name is not null) Account = name;
        }
        if (changed) Persist();
        return name;
    }

    /// <summary>GET /me/player (state + item + shuffle/repeat). Null when nothing is playing on any device.</summary>
    public async Task<SpotifyPlayback?> GetPlaybackAsync(CancellationToken ct)
    {
        var body = await SendAsync(HttpMethod.Get, "/me/player?additional_types=track,episode", ct).ConfigureAwait(false);
        try { return SpotifyJson.ParsePlayback(body); }
        catch (JsonException) { return null; }
    }

    /// <summary>GET /me/library/contains?uris=… for one item.</summary>
    public async Task<bool> IsInLibraryAsync(string uri, CancellationToken ct)
    {
        var body = await SendAsync(HttpMethod.Get, "/me/library/contains?uris=" + Uri.EscapeDataString(uri), ct).ConfigureAwait(false);
        try
        {
            var result = SpotifyJson.ParseContains(body);
            return result.Length > 0 && result[0];
        }
        catch (Exception ex) when (ex is JsonException or FormatException) { throw new SpotifyException("bad_response", inner: ex); }
    }

    /// <summary>PUT / DELETE /me/library?uris=… ("Liked Songs" for tracks).</summary>
    public Task SetInLibraryAsync(string uri, bool saved, CancellationToken ct) =>
        SendAsync(saved ? HttpMethod.Put : HttpMethod.Delete, "/me/library?uris=" + Uri.EscapeDataString(uri), ct);

    /// <summary>PUT /me/player/shuffle?state= (Premium only).</summary>
    public Task SetShuffleAsync(bool enabled, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "/me/player/shuffle?state=" + (enabled ? "true" : "false"), ct);

    /// <summary>PUT /me/player/repeat?state=track|context|off (Premium only).</summary>
    public Task SetRepeatAsync(string state, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "/me/player/repeat?state=" + state, ct);

    public void Dispose() => _tokenLock.Dispose();
}
