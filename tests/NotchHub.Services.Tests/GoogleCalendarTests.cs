using System.Net.Http;
using System.Text;
using System.Text.Json;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Services.Calendar;

namespace NotchHub.Services.Tests;

public sealed class GoogleCalendarTests : IDisposable
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notchhub-gtests-" + Guid.NewGuid().ToString("N"));

    public GoogleCalendarTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ───────────── PKCE / OAuth helpers ─────────────

    [Fact]
    public void Code_challenge_matches_rfc7636_appendix_b()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            GoogleOAuth.CreateCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Code_verifier_is_unreserved_and_random()
    {
        var a = GoogleOAuth.CreateCodeVerifier();
        var b = GoogleOAuth.CreateCodeVerifier();
        Assert.NotEqual(a, b);
        Assert.InRange(a.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.Matches("^[A-Za-z0-9_-]+$", GoogleOAuth.CreateState());
    }

    [Fact]
    public void Auth_url_contains_pkce_offline_and_scopes()
    {
        var url = GoogleOAuth.BuildAuthUrl("id.apps.googleusercontent.com", "http://127.0.0.1:5555/", "CHAL", "ST");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        var q = GoogleOAuth.ParseQuery(new Uri(url).Query);
        Assert.Equal("id.apps.googleusercontent.com", q["client_id"]);
        Assert.Equal("http://127.0.0.1:5555/", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.calendarlist.readonly openid email", q["scope"]);
        Assert.Equal("CHAL", q["code_challenge"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("ST", q["state"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("consent", q["prompt"]);
    }

    [Fact]
    public void Redirect_evaluation_checks_state_and_errors()
    {
        Assert.Equal("4/abc", GoogleOAuth.EvaluateRedirect(GoogleOAuth.ParseQuery("?code=4%2Fabc&state=s1"), "s1").Code);
        Assert.Equal("state_mismatch", GoogleOAuth.EvaluateRedirect(GoogleOAuth.ParseQuery("?code=x&state=evil"), "s1").Error);
        Assert.Equal("access_denied", GoogleOAuth.EvaluateRedirect(GoogleOAuth.ParseQuery("?error=access_denied&state=s1"), "s1").Error);
        Assert.Equal("no_code", GoogleOAuth.EvaluateRedirect(GoogleOAuth.ParseQuery("?state=s1"), "s1").Error);
    }

    [Fact]
    public void Token_response_and_id_token_email_are_parsed()
    {
        var payload = GoogleOAuth.Base64Url(Encoding.UTF8.GetBytes("""{"iss":"https://accounts.google.com","email":"jan.kowalski@gmail.com","email_verified":true}"""));
        var idToken = $"eyJhbGciOiJSUzI1NiJ9.{payload}.c2ln";
        var json = $$"""{"access_token":"ya29.a","expires_in":3599,"refresh_token":"1//r","scope":"x","token_type":"Bearer","id_token":"{{idToken}}"}""";
        var r = GoogleOAuth.ParseTokenResponse(json);
        Assert.Equal("ya29.a", r.AccessToken);
        Assert.Equal(3599, r.ExpiresInSeconds);
        Assert.Equal("1//r", r.RefreshToken);
        Assert.Null(r.Error);
        Assert.Equal("jan.kowalski@gmail.com", GoogleOAuth.EmailFromIdToken(r.IdToken));

        var err = GoogleOAuth.ParseTokenResponse("""{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
        Assert.Equal("invalid_grant", err.Error);
        Assert.Null(GoogleOAuth.EmailFromIdToken("garbage"));
    }

    [Fact]
    public async Task Loopback_receiver_returns_query_and_serves_localized_page()
    {
        using var receiver = new GoogleOAuth.LoopbackReceiver();
        Assert.StartsWith("http://127.0.0.1:", receiver.RedirectUri);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = receiver.WaitAsync(q => GoogleOAuth.ResultPage(GoogleOAuth.EvaluateRedirect(q, "s1").Code is not null, "pl"), cts.Token);

        using var http = new HttpClient();
        var favicon = await http.GetAsync(receiver.RedirectUri + "favicon.ico", cts.Token);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, favicon.StatusCode);
        var html = await http.GetStringAsync(receiver.RedirectUri + "?state=s1&code=4%2F0Ab&scope=email", cts.Token);
        var query = await wait;

        Assert.Equal("4/0Ab", query["code"]);
        Assert.Contains("Możesz zamknąć tę kartę", html);
    }

    // ───────────── Calendar API JSON ─────────────

    private const string CalendarListJson = """
        {
          "kind": "calendar#calendarList",
          "nextPageToken": "p2",
          "items": [
            { "id": "pl.polish#holiday@group.v.calendar.google.com", "summary": "Święta w Polsce", "backgroundColor": "#16a765", "accessRole": "reader" },
            { "id": "jan@gmail.com", "summary": "jan@gmail.com", "summaryOverride": "Jan", "backgroundColor": "#9fe1e7", "accessRole": "owner", "primary": true },
            { "id": "family@group.calendar.google.com", "summary": "Rodzina", "backgroundColor": "#f83a22", "accessRole": "writer" },
            { "id": "gone@group.calendar.google.com", "summary": "Old", "accessRole": "owner", "deleted": true }
          ]
        }
        """;

    [Fact]
    public void Calendar_list_is_parsed_with_colors_roles_and_hidden_flags()
    {
        var (list, next) = GoogleCalendarApi.ParseCalendarList(CalendarListJson, new HashSet<string> { "family@group.calendar.google.com" });
        Assert.Equal("p2", next);
        Assert.Equal(3, list.Count);
        var sorted = GoogleCalendarApi.SortCalendars(list);
        Assert.Equal(new[] { "jan@gmail.com", "family@group.calendar.google.com", "pl.polish#holiday@group.v.calendar.google.com" }, sorted.Select(c => c.Id));
        var primary = sorted[0];
        Assert.Equal("Jan", primary.Name);
        Assert.Equal("#9FE1E7", primary.ColorHex);
        Assert.True(primary.IsPrimary && primary.CanWrite && primary.IsEnabled);
        Assert.False(sorted[1].IsEnabled);
        Assert.True(sorted[1].CanWrite);
        Assert.False(sorted[2].CanWrite);
    }

    private const string EventsJson = """
        {
          "kind": "calendar#events",
          "items": [
            { "id": "timed1", "status": "confirmed", "summary": " Spotkanie ", "location": "Biuro",
              "start": { "dateTime": "2026-10-14T10:00:00+02:00", "timeZone": "Europe/Warsaw" },
              "end":   { "dateTime": "2026-10-14T11:30:00+02:00", "timeZone": "Europe/Warsaw" } },
            { "id": "allday1", "status": "confirmed", "summary": "Urlop",
              "start": { "date": "2026-10-20" }, "end": { "date": "2026-10-23" } },
            { "id": "utc1", "status": "tentative", "summary": "UTC call",
              "start": { "dateTime": "2026-10-26T09:00:00Z" }, "end": { "dateTime": "2026-10-26T09:30:00Z" } },
            { "id": "cancel1", "status": "cancelled" },
            { "id": "wl1", "eventType": "workingLocation", "summary": "Home",
              "start": { "date": "2026-10-15" }, "end": { "date": "2026-10-16" } }
          ]
        }
        """;

    [Fact]
    public void Events_are_mapped_timed_all_day_and_cancelled_skipped()
    {
        var (events, next) = GoogleCalendarApi.ParseEvents(EventsJson, "jan@gmail.com", "#9FE1E7", Warsaw);
        Assert.Null(next);
        Assert.Equal(3, events.Count);

        var timed = events[0];
        Assert.Equal("google:jan@gmail.com:timed1", timed.Id);
        Assert.Equal("google:jan@gmail.com", timed.SourceId);
        Assert.Equal("Spotkanie", timed.Title);
        Assert.Equal("Biuro", timed.Location);
        Assert.Equal("#9FE1E7", timed.ColorHex);
        Assert.False(timed.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), timed.Start);
        Assert.Equal(TimeSpan.FromMinutes(90), timed.End - timed.Start);

        var allDay = events[1];
        Assert.True(allDay.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(2)), allDay.Start);
        // Exclusive end date → midnight of 23 Oct (three days).
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.FromHours(2)), allDay.End);

        // 26 Oct is after the DST switch: 09:00Z = 10:00 CET.
        var utc = events[2];
        Assert.Equal(TimeSpan.FromHours(1), utc.Start.Offset);
        Assert.Equal(10, utc.Start.Hour);
    }

    [Fact]
    public void Event_ids_round_trip()
    {
        var id = GoogleCalendarApi.EventId("pl.polish#holiday@group.v.calendar.google.com", "abc_20261013");
        Assert.True(GoogleCalendarApi.TryParseEventId(id, out var cal, out var ev));
        Assert.Equal("pl.polish#holiday@group.v.calendar.google.com", cal);
        Assert.Equal("abc_20261013", ev);
        Assert.False(GoogleCalendarApi.TryParseEventId("local-guid", out _, out _));
    }

    [Fact]
    public void Insert_body_uses_exclusive_dates_for_all_day_and_zone_for_timed()
    {
        var start = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(2));
        using (var doc = JsonDocument.Parse(GoogleCalendarApi.BuildInsertBody("Urlop", start, start.AddDays(1), true, Warsaw)))
        {
            Assert.Equal("Urlop", doc.RootElement.GetProperty("summary").GetString());
            Assert.Equal("2026-10-20", doc.RootElement.GetProperty("start").GetProperty("date").GetString());
            Assert.Equal("2026-10-21", doc.RootElement.GetProperty("end").GetProperty("date").GetString());
        }
        // Same start and end on an all-day event still lasts one day.
        Assert.Equal((new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21)), GoogleCalendarApi.AllDayRange(start, start, Warsaw));

        var timedStart = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
        using (var doc = JsonDocument.Parse(GoogleCalendarApi.BuildInsertBody("Call", timedStart, timedStart.AddHours(1), false, Warsaw)))
        {
            var s = doc.RootElement.GetProperty("start");
            Assert.Equal("2026-10-14T10:00:00+02:00", s.GetProperty("dateTime").GetString());
            Assert.Equal("Europe/Warsaw", s.GetProperty("timeZone").GetString());
            Assert.Equal("2026-10-14T11:00:00+02:00", doc.RootElement.GetProperty("end").GetProperty("dateTime").GetString());
        }
    }

    [Fact]
    public void Api_error_codes_are_extracted()
    {
        Assert.Equal("PERMISSION_DENIED", GoogleCalendarApi.ParseErrorCode("""{"error":{"code":403,"message":"Forbidden","status":"PERMISSION_DENIED"}}"""));
        Assert.Equal("invalid_grant", GoogleCalendarApi.ParseErrorCode("""{"error":"invalid_grant"}"""));
        Assert.Null(GoogleCalendarApi.ParseErrorCode("<html>"));
    }

    // ───────────── service state (no network) ─────────────

    [Fact]
    public async Task Service_reports_configuration_state_and_routes_local_events()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), Warsaw);
        var settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        using var calendar = new CalendarService(settings, Path.Combine(_dir, "calendar.json"), Path.Combine(_dir, "cache"), null, time,
            Path.Combine(_dir, "google-token.bin"));

        // Without an own client id the built-in client is used when NotchHub ships one.
        Assert.Equal(BuiltInClients.HasGoogle ? GoogleConnectionState.Disconnected : GoogleConnectionState.NotConfigured, calendar.GoogleState);
        settings.Update(s => s.Calendar.GoogleClientId = "123.apps.googleusercontent.com");
        await calendar.StartAsync();
        Assert.Equal(GoogleConnectionState.Disconnected, calendar.GoogleState);
        Assert.Null(calendar.GoogleAccountEmail);
        Assert.Empty(calendar.GoogleCalendars);

        var now = time.GetUtcNow();
        var ev = await calendar.AddEventAsync("Lokalne", now.AddHours(1), now.AddHours(2), false, "local");
        Assert.NotNull(ev);
        Assert.Equal("local", ev!.SourceId);
        Assert.Null(await calendar.AddEventAsync("Google", now.AddHours(1), now.AddHours(2), false, "jan@gmail.com"));
        Assert.Single(calendar.GetEvents(now, now.AddDays(1)));

        Assert.False(await calendar.RemoveEventAsync("google:jan@gmail.com:abc"));
        Assert.False(await calendar.RemoveEventAsync("unknown"));
        Assert.True(await calendar.RemoveEventAsync(ev.Id));
        Assert.Empty(calendar.GetEvents(now, now.AddDays(1)));
        await calendar.DisconnectGoogleAsync(); // no token: must not touch the network
    }

    [Fact]
    public void Own_client_wins_over_built_in_and_built_in_is_the_fallback()
    {
        var own = new CalendarSettings { GoogleClientId = " own.apps.googleusercontent.com ", GoogleClientSecret = " s1 " };
        Assert.Equal(("own.apps.googleusercontent.com", "s1"), CalendarService.ResolveGoogleClient(own, "builtin", "bs"));
        var ownNoSecret = new CalendarSettings { GoogleClientId = "own" };
        Assert.Equal(("own", (string?)null), CalendarService.ResolveGoogleClient(ownNoSecret, "builtin", "bs"));
        var empty = new CalendarSettings { GoogleClientId = "  ", GoogleClientSecret = "ignored" };
        Assert.Equal(("builtin", "bs"), CalendarService.ResolveGoogleClient(empty, "builtin", "bs"));
        Assert.Equal(("builtin", (string?)null), CalendarService.ResolveGoogleClient(null, "builtin", ""));
        Assert.Equal(("", (string?)null), CalendarService.ResolveGoogleClient(empty, "", ""));
    }

    [Fact]
    public void Token_store_round_trips_with_dpapi()
    {
        var store = new GoogleTokenStore(Path.Combine(_dir, "calendar", "google-token.bin"));
        Assert.Null(store.Load());
        store.Save(new GoogleStoredToken("cid", "1//refresh", "jan@gmail.com"));
        var raw = File.ReadAllBytes(store.Path);
        Assert.DoesNotContain("1//refresh", Encoding.UTF8.GetString(raw));
        Assert.Equal(new GoogleStoredToken("cid", "1//refresh", "jan@gmail.com"), store.Load());
        store.Delete();
        Assert.Null(store.Load());
    }
}
