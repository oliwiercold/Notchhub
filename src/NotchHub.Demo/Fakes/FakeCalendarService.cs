using NotchHub.Core.Contracts;
using NotchHub.Demo.Infrastructure;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo calendar: a local source with a recurring daily event plus a Google account ("demo@gmail.com") with two
/// calendars. Add/remove/connect/disconnect work in memory; nothing touches the network.
/// </summary>
public sealed class FakeCalendarService : FakeBackgroundService, ICalendarService
{
    private const string DailyId = "demo-daily-ai-news";
    private const string PrimaryId = "demo@gmail.com";
    private const string FamilyId = "family0123456789@group.calendar.google.com";
    private readonly List<CalendarEvent> _events = [];
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private bool _connected = true;

    /// <summary>Seeds the events relative to today.</summary>
    public FakeCalendarService()
    {
        var today = DemoTime.Today;
        _events.Add(new("demo-team-sync", "Team sync", DemoTime.At(today.AddDays(1), 14, 0), DemoTime.At(today.AddDays(1), 15, 0), false, "Teams", "Weekly sync", "local", "#4285F4"));
        _events.Add(new("demo-dentist", "Dentist", DemoTime.At(today.AddDays(3), 9, 30), DemoTime.At(today.AddDays(3), 10, 15), false, "ul. Marszałkowska 87", null, "local", "#34A853"));
        _events.Add(new("demo-mom-birthday", "Mom's birthday", DemoTime.At(today.AddDays(5), 0, 0), DemoTime.At(today.AddDays(6), 0, 0), true, null, null, "local", "#FBBC04"));
        _events.Add(new($"google:{PrimaryId}:gym", "Siłownia", DemoTime.At(today.AddDays(2), 18, 0), DemoTime.At(today.AddDays(2), 19, 30), false, "CityFit", null, $"google:{PrimaryId}", "#7986CB"));
        _events.Add(new($"google:{FamilyId}:dinner", "Obiad u babci", DemoTime.At(today.AddDays(4), 13, 0), DemoTime.At(today.AddDays(4), 15, 0), false, null, null, $"google:{FamilyId}", "#33B679"));
    }

    /// <inheritdoc />
    public IReadOnlyList<CalendarSourceInfo> Sources { get; } = [new("local", "NotchHub", "#E91E63", true, null, null)];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public GoogleConnectionState GoogleState
    {
        get { lock (Gate) return _connected ? GoogleConnectionState.Connected : GoogleConnectionState.Disconnected; }
    }

    /// <inheritdoc />
    public string? GoogleAccountEmail
    {
        get { lock (Gate) return _connected ? PrimaryId : null; }
    }

    /// <inheritdoc />
    public string? GoogleError => null;

    /// <inheritdoc />
    public IReadOnlyList<GoogleCalendarInfo> GoogleCalendars
    {
        get
        {
            lock (Gate)
            {
                if (!_connected) return [];
                return
                [
                    new(PrimaryId, PrimaryId, "#7986CB", true, !_hidden.Contains(PrimaryId), true),
                    new(FamilyId, "Rodzina", "#33B679", false, !_hidden.Contains(FamilyId), true),
                ];
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CalendarEvent> GetEvents(DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<CalendarEvent>();
        for (var d = DateOnly.FromDateTime(from.LocalDateTime).AddDays(-1); d <= DateOnly.FromDateTime(to.LocalDateTime); d = d.AddDays(1))
            result.Add(new($"{DailyId}-{d:yyyyMMdd}", "Daily AI news research", DemoTime.At(d, 11, 0), DemoTime.At(d, 11, 30), false, null, null, "local", "#E91E63"));
        lock (Gate) result.AddRange(_events.Where(IsVisible));
        return result.Where(e => e.Start < to && e.End > from).OrderBy(e => e.Start).ThenBy(e => e.IsAllDay).ToArray();
    }

    /// <inheritdoc />
    public CalendarEvent? GetNextEvent(DateTimeOffset now)
    {
        var events = GetEvents(now, now.AddDays(7));
        return events.FirstOrDefault(e => !e.IsAllDay) ?? events.FirstOrDefault();
    }

    /// <inheritdoc />
    public CalendarEvent AddLocalEvent(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? colorHex = null)
    {
        var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), title, start, end < start ? start : end, isAllDay, null, null, "local", colorHex ?? "#E91E63");
        lock (Gate) _events.Add(ev);
        Raise(Changed);
        return ev;
    }

    /// <inheritdoc />
    public void RemoveLocalEvent(string id)
    {
        bool removed;
        lock (Gate) removed = _events.RemoveAll(e => e.Id == id && e.SourceId == "local") > 0;
        if (removed) Raise(Changed);
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> ConnectGoogleAsync(CancellationToken ct = default)
    {
        lock (Gate) _connected = true;
        Raise(Changed);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DisconnectGoogleAsync()
    {
        lock (Gate) _connected = false;
        Raise(Changed);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void SetGoogleCalendarEnabled(string calendarId, bool enabled)
    {
        lock (Gate)
        {
            if (enabled ? !_hidden.Remove(calendarId) : !_hidden.Add(calendarId)) return;
        }
        Raise(Changed);
    }

    /// <inheritdoc />
    public Task<CalendarEvent?> AddEventAsync(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? calendarId = null, string? colorHex = null)
    {
        if (string.IsNullOrEmpty(calendarId) || calendarId == "local")
            return Task.FromResult<CalendarEvent?>(AddLocalEvent(title, start, end, isAllDay, colorHex));
        var calendar = GoogleCalendars.FirstOrDefault(c => c.Id == calendarId && c.CanWrite);
        if (calendar is null) return Task.FromResult<CalendarEvent?>(null);
        var ev = new CalendarEvent($"google:{calendarId}:{Guid.NewGuid():N}", title, start, end < start ? start : end, isAllDay,
            null, null, $"google:{calendarId}", calendar.ColorHex);
        lock (Gate) _events.Add(ev);
        Raise(Changed);
        return Task.FromResult<CalendarEvent?>(ev);
    }

    /// <inheritdoc />
    public Task<bool> RemoveEventAsync(string eventId)
    {
        bool removed;
        lock (Gate) removed = _events.RemoveAll(e => e.Id == eventId && (e.SourceId == "local" || _connected)) > 0;
        if (removed) Raise(Changed);
        return Task.FromResult(removed);
    }

    private bool IsVisible(CalendarEvent e)
    {
        if (!e.SourceId.StartsWith("google:", StringComparison.Ordinal)) return true;
        return _connected && !_hidden.Contains(e.SourceId["google:".Length..]);
    }
}
