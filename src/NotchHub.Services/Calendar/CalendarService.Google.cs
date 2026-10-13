using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Services.Common;

namespace NotchHub.Services.Calendar;

/// <summary>
/// Google Calendar part: OAuth 2.0 installed-app flow (PKCE S256 + state, loopback redirect on 127.0.0.1) with the
/// user's own Desktop client from <see cref="CalendarSettings"/>, DPAPI-protected refresh token, Calendar API v3
/// calendarList/events (window −7 … +60 days) merged with local + ICS events, and insert/delete of events.
/// </summary>
public sealed partial class CalendarService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(5);

    private readonly GoogleTokenStore _googleStore;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private GoogleStoredToken? _googleToken;
    private string? _accessToken;
    private DateTimeOffset _accessExpiry;
    private string _googleClientId = "";
    private HashSet<string> _googleHidden = new(StringComparer.Ordinal);
    private string? _googleSettingsKey;
    private bool _googleConnecting;
    private bool _googleConnectFailed;
    private string? _googleError;
    private CancellationTokenSource? _connectCts;
    private IReadOnlyList<GoogleCalendarInfo> _googleCalendars = [];
    private Dictionary<string, IReadOnlyList<CalendarEvent>> _googleEvents = new(StringComparer.Ordinal);

    public GoogleConnectionState GoogleState
    {
        get
        {
            lock (_gate)
            {
                if (_googleClientId.Length == 0) return GoogleConnectionState.NotConfigured;
                if (_googleConnecting) return GoogleConnectionState.Connecting;
                if (_googleToken is not null) return GoogleConnectionState.Connected;
                return _googleConnectFailed ? GoogleConnectionState.Error : GoogleConnectionState.Disconnected;
            }
        }
    }

    public string? GoogleAccountEmail
    {
        get { lock (_gate) return GoogleActive ? _googleToken!.Email : null; }
    }

    public string? GoogleError
    {
        get { lock (_gate) return _googleError; }
    }

    public IReadOnlyList<GoogleCalendarInfo> GoogleCalendars
    {
        get { lock (_gate) return GoogleActive ? _googleCalendars : []; }
    }

    /// <summary>Signed in with the configured client (call under <see cref="_gate"/>).</summary>
    private bool GoogleActive => _googleToken is not null && _googleClientId.Length > 0;

    // ───────────────────────────── connect / disconnect ─────────────────────────────

    public async Task<bool> ConnectGoogleAsync(CancellationToken ct = default)
    {
        string clientId;
        CancellationTokenSource connectCts;
        lock (_gate)
        {
            if (_disposed || _googleClientId.Length == 0 || _googleConnecting) return false;
            clientId = _googleClientId;
            _googleConnecting = true;
            _googleConnectFailed = false;
            _googleError = null;
            _connectCts = connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        }
        RaiseChanged();
        var secret = _settings.Current.Calendar?.GoogleClientSecret?.Trim();
        var language = _settings.Current.Language ?? "pl";
        string? failure = null;
        try
        {
            connectCts.CancelAfter(ConnectTimeout);
            using var receiver = new GoogleOAuth.LoopbackReceiver();
            var verifier = GoogleOAuth.CreateCodeVerifier();
            var state = GoogleOAuth.CreateState();
            var url = GoogleOAuth.BuildAuthUrl(clientId, receiver.RedirectUri, GoogleOAuth.CreateCodeChallenge(verifier), state);
            Log.Info($"CalendarService: opening Google sign-in (loopback port {receiver.Port})");
            using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }

            var query = await receiver.WaitAsync(
                q => GoogleOAuth.ResultPage(GoogleOAuth.EvaluateRedirect(q, state).Code is not null, language),
                connectCts.Token).ConfigureAwait(false);
            var redirect = GoogleOAuth.EvaluateRedirect(query, state);
            if (redirect.Code is null)
            {
                failure = redirect.Error ?? "auth_failed";
                return false;
            }

            var form = new Dictionary<string, string>
            {
                ["code"] = redirect.Code,
                ["client_id"] = clientId,
                ["redirect_uri"] = receiver.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = verifier,
            };
            if (!string.IsNullOrEmpty(secret)) form["client_secret"] = secret;
            var tokens = await PostTokenAsync(form, connectCts.Token).ConfigureAwait(false);
            if (tokens.Error is not null || string.IsNullOrEmpty(tokens.AccessToken))
            {
                failure = tokens.Error ?? "token_failed";
                return false;
            }
            if (string.IsNullOrEmpty(tokens.RefreshToken))
            {
                failure = "no_refresh_token";
                return false;
            }

            var stored = new GoogleStoredToken(clientId, tokens.RefreshToken, GoogleOAuth.EmailFromIdToken(tokens.IdToken));
            _googleStore.Save(stored);
            lock (_gate)
            {
                _googleToken = stored;
                _accessToken = tokens.AccessToken;
                _accessExpiry = _time.GetUtcNow().AddSeconds(Math.Max(60, tokens.ExpiresInSeconds));
                _googleCalendars = [];
                _googleEvents = new(StringComparer.Ordinal);
                _googleConnecting = false;
            }
            Log.Info("CalendarService: connected to Google Calendar");
            RaiseChanged();
            await RefreshAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            failure = ct.IsCancellationRequested || _cts.IsCancellationRequested ? "cancelled" : "timeout";
            return false;
        }
        catch (Exception ex)
        {
            failure = ServiceHttp.ErrorCode(ex, ct);
            if (ServiceHttp.IsExpectedNetworkFailure(ex)) Log.Warn($"CalendarService: Google sign-in failed ({failure})");
            else Log.Warn("CalendarService: Google sign-in failed", ex);
            return false;
        }
        finally
        {
            lock (_gate)
            {
                _googleConnecting = false;
                if (failure is not null)
                {
                    _googleConnectFailed = _googleToken is null;
                    _googleError = failure;
                }
                if (ReferenceEquals(_connectCts, connectCts)) _connectCts = null;
            }
            connectCts.Dispose();
            if (failure is not null) Log.Warn($"CalendarService: Google sign-in not completed ({failure})");
            RaiseChanged();
        }
    }

    public async Task DisconnectGoogleAsync()
    {
        GoogleStoredToken? token;
        lock (_gate)
        {
            token = _googleToken;
            ClearGoogleData();
            _googleError = null;
            _googleConnectFailed = false;
            try { _connectCts?.Cancel(); } catch (ObjectDisposedException) { }
        }
        _googleStore.Delete();
        RaiseChanged();
        if (token is null) return;
        try
        {
            using var content = new FormUrlEncodedContent([new("token", token.RefreshToken)]);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await SharedHttp.PostAsync(GoogleOAuth.RevokeEndpoint, content, timeout.Token).ConfigureAwait(false);
            Log.Info($"CalendarService: Google token revoked ({(int)response.StatusCode})");
        }
        catch (Exception ex)
        {
            Log.Warn($"CalendarService: Google token revoke failed ({ServiceHttp.ErrorCode(ex, CancellationToken.None)})");
        }
    }

    public void SetGoogleCalendarEnabled(string calendarId, bool enabled)
    {
        if (string.IsNullOrEmpty(calendarId)) return;
        _settings.Update(s =>
        {
            s.Calendar ??= new CalendarSettings();
            s.Calendar.GoogleHiddenCalendars ??= [];
            var hidden = s.Calendar.GoogleHiddenCalendars;
            if (enabled) hidden.RemoveAll(id => id == calendarId);
            else if (!hidden.Contains(calendarId)) hidden.Add(calendarId);
        });
    }

    // ───────────────────────────── add / remove ─────────────────────────────

    public async Task<CalendarEvent?> AddEventAsync(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay,
        string? calendarId = null, string? colorHex = null)
    {
        if (string.IsNullOrEmpty(calendarId) || calendarId == LocalSourceId)
            return AddLocalEvent(title, start, end, isAllDay, colorHex);

        GoogleCalendarInfo? calendar;
        lock (_gate) calendar = GoogleActive ? _googleCalendars.FirstOrDefault(c => c.Id == calendarId && c.CanWrite) : null;
        if (calendar is null) return null;

        var tz = _time.LocalTimeZone;
        var body = GoogleCalendarApi.BuildInsertBody((title ?? "").Trim(), start, end, isAllDay, tz);
        try
        {
            using var response = await SendGoogleAsync(HttpMethod.Post, GoogleCalendarApi.InsertUrl(calendarId), body, _cts.Token).ConfigureAwait(false);
            if (response is null) return null;
            var json = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                SetGoogleError(GoogleCalendarApi.ParseErrorCode(json) ?? $"http_{(int)response.StatusCode}", "insert");
                return null;
            }
            var ev = GoogleCalendarApi.ParseEvent(json, calendarId, calendar.ColorHex, tz);
            if (ev is null) return null;
            lock (_gate)
            {
                var list = _googleEvents.TryGetValue(calendarId, out var existing) ? existing.ToList() : [];
                list.RemoveAll(e => e.Id == ev.Id);
                list.Add(ev);
                list.Sort(CompareEvents);
                _googleEvents[calendarId] = list;
                _googleError = null;
            }
            RaiseChanged();
            return ev;
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            SetGoogleError(ServiceHttp.ErrorCode(ex, _cts.Token), "insert", ex);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<bool> RemoveEventAsync(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return false;
        if (!GoogleCalendarApi.TryParseEventId(eventId, out var calendarId, out var googleEventId))
        {
            bool isLocal;
            lock (_gate) isLocal = _local.Any(e => e.Id == eventId);
            if (!isLocal) return false;
            RemoveLocalEvent(eventId);
            return true;
        }

        bool writable;
        lock (_gate) writable = GoogleActive && _googleCalendars.Any(c => c.Id == calendarId && c.CanWrite);
        if (!writable) return false;
        try
        {
            using var response = await SendGoogleAsync(HttpMethod.Delete, GoogleCalendarApi.DeleteUrl(calendarId, googleEventId), null, _cts.Token)
                .ConfigureAwait(false);
            if (response is null) return false;
            if (!response.IsSuccessStatusCode && response.StatusCode is not (HttpStatusCode.Gone or HttpStatusCode.NotFound))
            {
                var json = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(false);
                SetGoogleError(GoogleCalendarApi.ParseErrorCode(json) ?? $"http_{(int)response.StatusCode}", "delete");
                return false;
            }
            lock (_gate)
            {
                if (_googleEvents.TryGetValue(calendarId, out var existing))
                    _googleEvents[calendarId] = existing.Where(e => e.Id != eventId).ToArray();
                _googleError = null;
            }
            RaiseChanged();
            return true;
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            SetGoogleError(ServiceHttp.ErrorCode(ex, _cts.Token), "delete", ex);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // ───────────────────────────── refresh ─────────────────────────────

    private void LoadGoogleToken()
    {
        var token = _googleStore.Load();
        lock (_gate) _googleToken = token;
    }

    /// <summary>Copies client id + hidden calendars from the settings. True when something changed.</summary>
    private bool SyncGoogleSettings()
    {
        var cal = _settings.Current.Calendar;
        var clientId = cal?.GoogleClientId?.Trim() ?? "";
        var hidden = new HashSet<string>(cal?.GoogleHiddenCalendars ?? [], StringComparer.Ordinal);
        var key = clientId + "\n" + string.Join("\n", hidden.Order(StringComparer.Ordinal));
        var forget = false;
        lock (_gate)
        {
            if (key == _googleSettingsKey) return false;
            _googleSettingsKey = key;
            _googleClientId = clientId;
            _googleHidden = hidden;
            if (_googleToken is not null && clientId.Length > 0 && _googleToken.ClientId != clientId)
            {
                // A grant is bound to its OAuth client: a new client id means signing in again.
                ClearGoogleData();
                forget = true;
            }
            _googleCalendars = _googleCalendars.Select(c => c with { IsEnabled = !hidden.Contains(c.Id) }).ToArray();
            if (clientId.Length == 0) _googleConnectFailed = false;
        }
        if (forget) _googleStore.Delete();
        return true;
    }

    /// <summary>Call under <see cref="_gate"/>.</summary>
    private void ClearGoogleData()
    {
        _googleToken = null;
        _accessToken = null;
        _accessExpiry = default;
        _googleCalendars = [];
        _googleEvents = new(StringComparer.Ordinal);
    }

    /// <summary>Google events of enabled calendars (call under <see cref="_gate"/>).</summary>
    private IEnumerable<CalendarEvent> GoogleEventsUnderLock()
    {
        if (!GoogleActive) return [];
        return _googleEvents.Where(p => !_googleHidden.Contains(p.Key)).SelectMany(p => p.Value);
    }

    private async Task RefreshGoogleAsync(CancellationToken ct)
    {
        lock (_gate)
            if (!GoogleActive || _googleConnecting) return;
        try
        {
            var calendars = new List<GoogleCalendarInfo>();
            HashSet<string> hidden;
            lock (_gate) hidden = _googleHidden;
            string? page = null;
            do
            {
                var json = await GetGoogleJsonAsync(GoogleCalendarApi.CalendarListUrl(page), ct).ConfigureAwait(false);
                if (json is null) return;
                var (items, next) = GoogleCalendarApi.ParseCalendarList(json, hidden);
                calendars.AddRange(items);
                page = next;
            } while (page is not null);
            var sorted = GoogleCalendarApi.SortCalendars(calendars);
            lock (_gate) _googleCalendars = sorted;

            var now = _time.GetUtcNow();
            var tz = _time.LocalTimeZone;
            string? firstError = null;
            var results = await Task.WhenAll(sorted.Where(c => c.IsEnabled).Select(async c =>
            {
                try
                {
                    var events = new List<CalendarEvent>();
                    string? p = null;
                    do
                    {
                        var json = await GetGoogleJsonAsync(GoogleCalendarApi.EventsUrl(c.Id, now - PastWindow, now + FutureWindow, p), ct)
                            .ConfigureAwait(false);
                        if (json is null) return (c.Id, (IReadOnlyList<CalendarEvent>?)null);
                        var (items, next) = GoogleCalendarApi.ParseEvents(json, c.Id, c.ColorHex, tz);
                        events.AddRange(items);
                        p = next;
                    } while (p is not null && events.Count < 20_000);
                    events.Sort(CompareEvents);
                    return (c.Id, (IReadOnlyList<CalendarEvent>?)events);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var code = ServiceHttp.ErrorCode(ex, ct);
                    Interlocked.CompareExchange(ref firstError, code, null);
                    Log.Warn($"CalendarService: Google events download failed ({code})");
                    return (c.Id, (IReadOnlyList<CalendarEvent>?)null);
                }
            })).ConfigureAwait(false);

            lock (_gate)
            {
                if (!GoogleActive) return;
                var map = new Dictionary<string, IReadOnlyList<CalendarEvent>>(StringComparer.Ordinal);
                foreach (var (id, events) in results)
                {
                    // Keep the last known events of a calendar whose download failed.
                    if (events is not null) map[id] = events;
                    else if (_googleEvents.TryGetValue(id, out var previous)) map[id] = previous;
                }
                _googleEvents = map;
                _googleError = firstError;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            SetGoogleError(ServiceHttp.ErrorCode(ex, ct), "refresh", ex);
        }
    }

    // ───────────────────────────── HTTP ─────────────────────────────

    private async Task<string?> GetGoogleJsonAsync(string url, CancellationToken ct)
    {
        using var response = await SendGoogleAsync(HttpMethod.Get, url, null, ct).ConfigureAwait(false);
        if (response is null) return null;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google API {(int)response.StatusCode} {GoogleCalendarApi.ParseErrorCode(body)}", null, response.StatusCode);
        return body;
    }

    /// <summary>Sends an authorized request (refreshing the access token, one retry on 401). Null when signed out.</summary>
    private async Task<HttpResponseMessage?> SendGoogleAsync(HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 0, ct).ConfigureAwait(false);
            if (token is null) return null;
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (jsonBody is not null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var response = await SharedHttp.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                continue;
            }
            return response;
        }
    }

    private async Task<string?> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GoogleStoredToken? token;
            lock (_gate)
            {
                token = GoogleActive ? _googleToken : null;
                if (token is null) return null;
                if (!forceRefresh && _accessToken is not null && _accessExpiry > _time.GetUtcNow().AddMinutes(1)) return _accessToken;
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"] = token.ClientId,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token",
            };
            var secret = _settings.Current.Calendar?.GoogleClientSecret?.Trim();
            if (!string.IsNullOrEmpty(secret)) form["client_secret"] = secret;
            var result = await PostTokenAsync(form, ct).ConfigureAwait(false);

            if (result.Error is "invalid_grant" or "invalid_client" or "unauthorized_client")
            {
                LoseGrant(token, result.Error);
                return null;
            }
            if (result.Error is not null || string.IsNullOrEmpty(result.AccessToken))
                throw new HttpRequestException($"Token refresh failed ({result.Error ?? "no_access_token"})");

            lock (_gate)
            {
                if (!ReferenceEquals(_googleToken, token)) return null;
                _accessToken = result.AccessToken;
                _accessExpiry = _time.GetUtcNow().AddSeconds(Math.Max(60, result.ExpiresInSeconds));
                return _accessToken;
            }
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static async Task<GoogleTokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await SharedHttp.PostAsync(GoogleOAuth.TokenEndpoint, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        GoogleTokenResponse parsed;
        try
        {
            parsed = GoogleOAuth.ParseTokenResponse(body);
        }
        catch (System.Text.Json.JsonException) when (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Token endpoint error", null, response.StatusCode);
        }
        if (!response.IsSuccessStatusCode && parsed.Error is null)
            throw new HttpRequestException("Token endpoint error", null, response.StatusCode);
        return parsed;
    }

    /// <summary>The refresh token was revoked/expired: forget it and go back to Disconnected with the error code.</summary>
    private void LoseGrant(GoogleStoredToken token, string code)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_googleToken, token)) return;
            ClearGoogleData();
            _googleError = code;
        }
        _googleStore.Delete();
        Log.Warn($"CalendarService: Google grant no longer valid ({code}); signed out");
        RaiseChanged();
    }

    private void SetGoogleError(string code, string operation, Exception? ex = null)
    {
        lock (_gate) _googleError = code;
        if (ex is null || ServiceHttp.IsExpectedNetworkFailure(ex)) Log.Warn($"CalendarService: Google {operation} failed ({code})");
        else Log.Warn($"CalendarService: Google {operation} failed", ex);
        RaiseChanged();
    }
}
