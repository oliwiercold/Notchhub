using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Today;

/// <summary>
/// Expanded day agenda: clicking a day in the week strip (or the compact event card) grows the events card into a
/// scrollable list of every event of that day; clicking the day again / Esc / the collapse button folds it back.
/// </summary>
public sealed partial class TodayViewModel
{
    public ObservableCollection<EventItem> DayEvents { get; } = [];
    [ObservableProperty] private bool _isDayExpanded;
    [ObservableProperty] private string _dayTitle = "";
    [ObservableProperty] private string _dayCount = "";
    [ObservableProperty] private bool _dayHasNoEvents;

    /// <summary>Week-strip click: selects the day and expands its agenda; clicking the expanded day again collapses.</summary>
    private void OnDayClicked(DayItem day)
    {
        var same = day.Date == _selected;
        _selected = day.Date;
        foreach (var d in Days) d.IsSelected = d.Date == _selected && !d.IsToday;
        RefreshEvents();
        if (IsDayExpanded && same) IsDayExpanded = false;
        else ExpandDay();
    }

    /// <summary>Compact card click.</summary>
    [RelayCommand]
    private void ToggleDay()
    {
        if (IsDayExpanded) IsDayExpanded = false;
        else ExpandDay();
    }

    [RelayCommand] private void CollapseDay() => IsDayExpanded = false;

    private void ExpandDay()
    {
        RefreshDayList(force: true);
        IsDayExpanded = true;
    }

    /// <summary>Called from RefreshEvents (calendar change, minute tick, day selection) — rebuilds only while expanded.</summary>
    private void RefreshDayList(bool force = false)
    {
        if (!IsDayExpanded && !force) return;
        DayEvents.Clear();
        var day = _selected == default ? DateOnly.FromDateTime(DateTime.Now) : _selected;
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue));
        var to = from.AddDays(1);
        IReadOnlyList<CalendarEvent> events = [];
        IReadOnlyList<CalendarSourceInfo> sources = [];
        IReadOnlyList<GoogleCalendarInfo> google = [];
        try
        {
            var cal = AppServices.Calendar;
            events = cal.GetEvents(from, to);
            sources = cal.Sources;
            google = cal.GoogleCalendars;
        }
        catch (Exception ex) { Log.Warn("Today: reading day events failed", ex); }

        var now = DateTimeOffset.Now;
        foreach (var e in events.OrderBy(e => e.IsAllDay ? 0 : 1).ThenBy(e => e.Start))
        {
            var (label, name, sourceColor) = Source(e.SourceId, sources, google);
            string startText, endText;
            if (e.IsAllDay)
            {
                startText = Loc.Get("today.allDay");
                endText = "";
            }
            else
            {
                startText = e.Start < from ? "…" + FormatTime(e.Start) : FormatTime(e.Start);
                endText = e.End > to ? FormatTime(e.End) + "…" : FormatTime(e.End);
            }
            DayEvents.Add(new EventItem
            {
                Id = e.Id,
                Header = startText,
                EndText = endText,
                Title = string.IsNullOrWhiteSpace(e.Title) ? Loc.Get("today.day.untitled") : e.Title,
                Location = string.IsNullOrWhiteSpace(e.Location) ? null : e.Location.Trim(),
                IsLocal = e.SourceId == "local",
                CanDelete = e.SourceId == "local" || IsWritableGoogle(e.SourceId),
                ColorBrush = BrushUtil.FromHex(e.ColorHex) ?? BrushUtil.FromHex(sourceColor),
                SourceLabel = label,
                SourceName = name,
                IsNow = !e.IsAllDay && e.Start <= now && e.End > now,
                IsPast = e.End <= now,
            });
        }

        DayHasNoEvents = DayEvents.Count == 0;
        DayCount = CountText(DayEvents.Count);
        var title = day.ToDateTime(TimeOnly.MinValue).ToString(Loc.Get("today.day.titleFormat"), Culture);
        if (day == _today) title = $"{Loc.Get("today.day.today")} • {title}";
        else if (day == _today.AddDays(1)) title = $"{Loc.Get("today.tomorrowChip")} • {title}";
        DayTitle = title.Length > 0 ? char.ToUpper(title[0], Culture) + title[1..] : title;
    }

    private static (string Label, string Name, string? Color) Source(string sourceId, IReadOnlyList<CalendarSourceInfo> sources,
        IReadOnlyList<GoogleCalendarInfo> google)
    {
        if (sourceId == "local") return (Loc.Get("today.source.local"), Loc.Get("today.event.local"), null);
        if (sourceId.StartsWith("google:", StringComparison.Ordinal))
        {
            var g = google.FirstOrDefault(c => sourceId.Length == 7 + c.Id.Length && sourceId.EndsWith(c.Id, StringComparison.Ordinal));
            return ("Google", g?.Name ?? "Google Calendar", g?.ColorHex);
        }
        var s = sources.FirstOrDefault(x => x.Id == sourceId);
        return ("ICS", string.IsNullOrWhiteSpace(s?.Name) ? "ICS" : s.Name, s?.ColorHex);
    }

    /// <summary>"1 wydarzenie / 3 wydarzenia / 5 wydarzeń" — Polish plural rules; English one/other.</summary>
    private static string CountText(int n)
    {
        string key;
        if (n == 1) key = "today.day.count1";
        else if (!English && n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) key = "today.day.countFew";
        else key = "today.day.countMany";
        return Loc.Format(key, n);
    }
}
