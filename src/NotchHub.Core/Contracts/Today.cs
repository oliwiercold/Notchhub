namespace NotchHub.Core.Contracts;

// ───────────────────────────── Weather ─────────────────────────────

/// <param name="Code">WMO weather interpretation code (Open-Meteo).</param>
public sealed record WeatherHour(DateTimeOffset Time, double TemperatureC, int Code, bool IsDay, double PrecipitationProbability);

public sealed record WeatherDay(DateOnly Date, double MinC, double MaxC, int Code, double PrecipitationProbability, DateTimeOffset? Sunrise, DateTimeOffset? Sunset);

/// <summary>Current conditions + short forecast. Temperatures always in °C (UI converts).</summary>
/// <param name="WeatherCode">WMO code; UI maps it to a localized description + icon.</param>
public sealed record WeatherSnapshot(
    DateTimeOffset FetchedAt,
    string LocationName,
    double TemperatureC,
    double ApparentTemperatureC,
    int WeatherCode,
    bool IsDay,
    double HumidityPercent,
    double WindKmh,
    double TodayMinC,
    double TodayMaxC,
    IReadOnlyList<WeatherHour> Hourly,
    IReadOnlyList<WeatherDay> Daily);

public interface IWeatherService : IBackgroundService
{
    WeatherSnapshot? Current { get; }
    bool IsLoading { get; }
    string? LastError { get; }
    event EventHandler? Changed;
    Task RefreshAsync(CancellationToken ct = default);
    /// <summary>Geocodes a city name (for the settings UI). Empty list when nothing found.</summary>
    Task<IReadOnlyList<GeoLocation>> SearchLocationAsync(string query, CancellationToken ct = default);
}

public sealed record GeoLocation(string Name, string? Region, string? Country, double Latitude, double Longitude);

// ───────────────────────────── Calendar ─────────────────────────────

/// <param name="SourceId">"local" or the calendar subscription id.</param>
public sealed record CalendarEvent(
    string Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    string SourceId,
    string? ColorHex);

/// <summary>A calendar of the connected Google account.</summary>
public sealed record GoogleCalendarInfo(string Id, string Name, string ColorHex, bool IsPrimary, bool IsEnabled, bool CanWrite);

public enum GoogleConnectionState { NotConfigured, Disconnected, Connecting, Connected, Error }

public sealed record CalendarSourceInfo(string Id, string Name, string ColorHex, bool IsLocal, DateTimeOffset? LastSync, string? Error);

public interface ICalendarService : IBackgroundService
{
    IReadOnlyList<CalendarSourceInfo> Sources { get; }
    event EventHandler? Changed;
    /// <summary>Events overlapping [from, to), recurrences expanded, sorted by start.</summary>
    IReadOnlyList<CalendarEvent> GetEvents(DateTimeOffset from, DateTimeOffset to);
    /// <summary>The ongoing event or the next one starting within 7 days (all-day events last).</summary>
    CalendarEvent? GetNextEvent(DateTimeOffset now);
    CalendarEvent AddLocalEvent(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? colorHex = null);
    void RemoveLocalEvent(string id);
    /// <summary>Re-downloads ICS subscriptions and Google calendars.</summary>
    Task RefreshAsync(CancellationToken ct = default);

    // ── Google Calendar (OAuth 2.0 installed-app flow with the user's own client id from settings.Calendar) ──

    /// <summary>NotConfigured = no client id in settings; Disconnected = configured but not signed in.</summary>
    GoogleConnectionState GoogleState { get; }
    string? GoogleAccountEmail { get; }
    /// <summary>Short error code of the last Google failure (UI localizes), null when fine.</summary>
    string? GoogleError { get; }
    /// <summary>All calendars of the account (IsEnabled = shown in NotchHub).</summary>
    IReadOnlyList<GoogleCalendarInfo> GoogleCalendars { get; }
    /// <summary>Opens the browser for Google sign-in (PKCE + loopback redirect) and stores the refresh token (DPAPI). True on success.</summary>
    Task<bool> ConnectGoogleAsync(CancellationToken ct = default);
    /// <summary>Revokes/forgets the token and removes Google events.</summary>
    Task DisconnectGoogleAsync();
    void SetGoogleCalendarEnabled(string calendarId, bool enabled);
    /// <summary>Adds an event to a writable Google calendar (calendarId) or locally (null / "local").</summary>
    Task<CalendarEvent?> AddEventAsync(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? calendarId = null, string? colorHex = null);
    /// <summary>Removes a local event or an event of a writable Google calendar. False when not possible.</summary>
    Task<bool> RemoveEventAsync(string eventId);
}

// ───────────────────────────── Todos ─────────────────────────────

public sealed record TodoItem(Guid Id, string Text, bool IsDone, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public interface ITodoService : IBackgroundService
{
    /// <summary>In display order. Items completed before today are removed automatically.</summary>
    IReadOnlyList<TodoItem> Items { get; }
    event EventHandler? Changed;
    TodoItem Add(string text);
    void SetDone(Guid id, bool done);
    void Rename(Guid id, string text);
    void Remove(Guid id);
    void Move(Guid id, int newIndex);
    void ClearCompleted();
}

// ───────────────────────────── Timer ─────────────────────────────

public enum TimerState { Idle, Running, Paused, Finished }

public interface ITimerService : IBackgroundService
{
    TimerState State { get; }
    /// <summary>Total duration of the current countdown.</summary>
    TimeSpan Duration { get; }
    TimeSpan Remaining { get; }
    /// <summary>State changes plus a tick roughly every 250 ms while running.</summary>
    event EventHandler? Changed;
    /// <summary>Raised once when the countdown reaches zero.</summary>
    event EventHandler? Finished;
    void Start(TimeSpan duration);
    void Pause();
    void Resume();
    void Reset();
    void AddTime(TimeSpan delta);
}
