using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Services.Calendar;
using NotchHub.Services.Timer;
using NotchHub.Services.Todos;

namespace NotchHub.Services.Tests;

internal sealed class FakeTime(DateTimeOffset now, TimeZoneInfo? tz = null) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone { get; } = tz ?? TimeZoneInfo.Local;
    public void Advance(TimeSpan delta) => Now += delta;
}

public sealed class TodayServicesTests : IDisposable
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notchhub-tests-" + Guid.NewGuid().ToString("N"));

    public TodayServicesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private const string Ics = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//test//EN
        BEGIN:VEVENT
        UID:weekly@test
        DTSTART;TZID=Europe/Warsaw:20261005T100000
        DTEND;TZID=Europe/Warsaw:20261005T110000
        RRULE:FREQ=WEEKLY;COUNT=5
        EXDATE;TZID=Europe/Warsaw:20261012T100000
        SUMMARY:Weekly
        END:VEVENT
        BEGIN:VEVENT
        UID:weekly@test
        RECURRENCE-ID;TZID=Europe/Warsaw:20261019T100000
        DTSTART;TZID=Europe/Warsaw:20261019T120000
        DTEND;TZID=Europe/Warsaw:20261019T130000
        SUMMARY:Weekly (moved)
        END:VEVENT
        BEGIN:VEVENT
        UID:allday@test
        DTSTART;VALUE=DATE:20261014
        DTEND;VALUE=DATE:20261015
        SUMMARY:Holiday
        END:VEVENT
        BEGIN:VEVENT
        UID:utc@test
        DTSTART:20261020T150000Z
        DTEND:20261020T160000Z
        SUMMARY:Utc
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Ics_expands_rrule_exdate_recurrence_id_tzid_and_all_day()
    {
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var events = CalendarService.ExpandIcs(Ics, "sub", "#FFFFFF", from, from.AddDays(60), Warsaw);

        var weekly = events.Where(e => e.Title.StartsWith("Weekly", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, weekly.Count); // 5 occurrences − 1 EXDATE
        Assert.DoesNotContain(weekly, e => e.Start.Date == new DateTime(2026, 10, 12));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero), weekly[0].Start.ToUniversalTime());
        var moved = Assert.Single(weekly, e => e.Title == "Weekly (moved)");
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero), moved.Start.ToUniversalTime());
        // After the DST switch (25 Oct) 10:00 Warsaw is 09:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), weekly[2].Start.ToUniversalTime());

        var allDay = Assert.Single(events, e => e.IsAllDay);
        Assert.Equal(new DateTime(2026, 10, 14), allDay.Start.DateTime);
        Assert.Equal(TimeSpan.FromDays(1), allDay.End - allDay.Start);

        var utc = Assert.Single(events, e => e.Title == "Utc");
        Assert.Equal(17, utc.Start.Hour); // 15:00Z = 17:00 CEST
        Assert.All(events, e => Assert.Equal("sub", e.SourceId));
    }

    [Fact]
    public void Next_event_prefers_ongoing_then_timed_before_all_day()
    {
        var now = new DateTimeOffset(2026, 10, 13, 9, 30, 0, Warsaw.GetUtcOffset(new DateTime(2026, 10, 13)));
        var time = new FakeTime(now, Warsaw);
        var settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        using var calendar = new CalendarService(settings, Path.Combine(_dir, "calendar.json"), Path.Combine(_dir, "cache"), null, time);

        calendar.AddLocalEvent("All day", now.Date, now.Date.AddDays(1), true);
        var later = calendar.AddLocalEvent("Later", now.AddHours(2), now.AddHours(3), false);
        calendar.AddLocalEvent("Tomorrow", now.AddDays(1), now.AddDays(1).AddHours(1), false);
        calendar.AddLocalEvent("Past", now.AddHours(-3), now.AddHours(-2), false);

        Assert.Equal(later.Id, calendar.GetNextEvent(now)?.Id);

        var ongoing = calendar.AddLocalEvent("Ongoing", now.AddMinutes(-10), now.AddMinutes(20), false);
        Assert.Equal(ongoing.Id, calendar.GetNextEvent(now)?.Id);

        // Evening: only the all-day event is left today, before tomorrow's timed event.
        Assert.Equal("All day", calendar.GetNextEvent(now.AddHours(10))?.Title);
        Assert.Null(calendar.GetNextEvent(now.AddDays(30)));
        Assert.Equal(5, calendar.GetEvents(now.AddDays(-1), now.AddDays(2)).Count);
    }

    [Fact]
    public void Todos_completed_before_today_are_removed()
    {
        var path = Path.Combine(_dir, "todos.json");
        var now = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.FromHours(2));
        var items = new[]
        {
            new TodoItem(Guid.NewGuid(), "old done", true, now.AddDays(-3), now.AddDays(-1)),
            new TodoItem(Guid.NewGuid(), "open", false, now.AddDays(-3), null),
            new TodoItem(Guid.NewGuid(), "done today", true, now.AddDays(-1), now.AddHours(-1)),
        };
        JsonStore.Save(path, new { Version = 1, Items = items });

        var time = new FakeTime(now, Warsaw);
        using var todos = new TodoService(path, time);
        Assert.Equal(["open", "done today"], todos.Items.Select(i => i.Text));

        var added = todos.Add("new");
        Assert.Equal(["open", "new", "done today"], todos.Items.Select(i => i.Text));
        todos.SetDone(added.Id, true);
        Assert.Equal(["open", "new", "done today"], todos.Items.Select(i => i.Text));

        time.Advance(TimeSpan.FromDays(1)); // next day: everything completed yesterday goes away
        todos.RemoveExpired();
        Assert.Equal(["open"], todos.Items.Select(i => i.Text));
    }

    [Fact]
    public void Timer_counts_down_pauses_and_finishes_once()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        using var timer = new TimerService(time);
        var finished = 0;
        timer.Finished += (_, _) => finished++;

        timer.Start(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(6), timer.Remaining);

        timer.Pause();
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimerState.Paused, timer.State);
        Assert.Equal(TimeSpan.FromSeconds(6), timer.Remaining);

        timer.Resume();
        timer.AddTime(TimeSpan.FromSeconds(4));
        time.Advance(TimeSpan.FromSeconds(9));
        timer.Tick();
        Assert.Equal(TimerState.Running, timer.State);
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Remaining);

        time.Advance(TimeSpan.FromSeconds(1));
        timer.Tick();
        timer.Tick();
        Assert.Equal(TimerState.Finished, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
        Assert.Equal(1, finished);
        Assert.Equal(TimeSpan.FromSeconds(14), timer.Duration);
    }
}
