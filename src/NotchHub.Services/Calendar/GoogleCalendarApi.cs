using System.Globalization;
using System.Text.Json;
using NotchHub.Core.Contracts;

namespace NotchHub.Services.Calendar;

/// <summary>Calendar API v3 JSON mapping (pure functions, unit-tested with sample responses).</summary>
internal static class GoogleCalendarApi
{
    public const string BaseUrl = "https://www.googleapis.com/calendar/v3";
    public const string IdPrefix = "google:";
    public const string DefaultColor = "#4285F4";

    public static string SourceId(string calendarId) => IdPrefix + calendarId;

    public static string EventId(string calendarId, string eventId) => $"{IdPrefix}{calendarId}:{eventId}";

    /// <summary>Splits "google:&lt;calendarId&gt;:&lt;eventId&gt;" (event ids never contain ':').</summary>
    public static bool TryParseEventId(string id, out string calendarId, out string eventId)
    {
        calendarId = eventId = "";
        if (!id.StartsWith(IdPrefix, StringComparison.Ordinal)) return false;
        var rest = id[IdPrefix.Length..];
        var i = rest.LastIndexOf(':');
        if (i <= 0 || i == rest.Length - 1) return false;
        calendarId = rest[..i];
        eventId = rest[(i + 1)..];
        return true;
    }

    public static string CalendarListUrl(string? pageToken) =>
        $"{BaseUrl}/users/me/calendarList?maxResults=250" + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

    public static string EventsUrl(string calendarId, DateTimeOffset timeMin, DateTimeOffset timeMax, string? pageToken) =>
        $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&orderBy=startTime&maxResults=2500" +
        $"&timeMin={Uri.EscapeDataString(Rfc3339(timeMin))}&timeMax={Uri.EscapeDataString(Rfc3339(timeMax))}" +
        (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

    public static string InsertUrl(string calendarId) => $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events";

    public static string DeleteUrl(string calendarId, string eventId) =>
        $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

    public static string Rfc3339(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    /// <summary>Parses one calendarList page. <paramref name="hidden"/> = ids disabled by the user.</summary>
    public static (List<GoogleCalendarInfo> Calendars, string? NextPageToken) ParseCalendarList(string json, ICollection<string> hidden)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = new List<GoogleCalendarInfo>();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = Str(item, "id");
                if (string.IsNullOrEmpty(id) || Bool(item, "deleted")) continue;
                var name = Str(item, "summaryOverride") ?? Str(item, "summary") ?? id;
                var color = NormalizeColor(Str(item, "backgroundColor"));
                var role = Str(item, "accessRole");
                list.Add(new GoogleCalendarInfo(id, name, color, Bool(item, "primary"), !hidden.Contains(id),
                    role is "owner" or "writer"));
            }
        }
        return (list, Str(root, "nextPageToken"));
    }

    /// <summary>Primary first, then writable, then by name.</summary>
    public static List<GoogleCalendarInfo> SortCalendars(IEnumerable<GoogleCalendarInfo> calendars) =>
        calendars.OrderByDescending(c => c.IsPrimary).ThenByDescending(c => c.CanWrite)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Parses one events page of a calendar (cancelled and working-location entries skipped).</summary>
    public static (List<CalendarEvent> Events, string? NextPageToken) ParseEvents(string json, string calendarId, string colorHex, TimeZoneInfo tz)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = new List<CalendarEvent>();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var ev = ParseEvent(item, calendarId, colorHex, tz);
                if (ev is not null) list.Add(ev);
            }
        }
        return (list, Str(root, "nextPageToken"));
    }

    public static CalendarEvent? ParseEvent(string json, string calendarId, string colorHex, TimeZoneInfo tz)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseEvent(doc.RootElement, calendarId, colorHex, tz);
    }

    private static CalendarEvent? ParseEvent(JsonElement item, string calendarId, string colorHex, TimeZoneInfo tz)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = Str(item, "id");
        if (string.IsNullOrEmpty(id)) return null;
        if (string.Equals(Str(item, "status"), "cancelled", StringComparison.OrdinalIgnoreCase)) return null;
        if (Str(item, "eventType") == "workingLocation") return null;
        if (!item.TryGetProperty("start", out var startEl) || !TryParseWhen(startEl, tz, out var start, out var allDay)) return null;
        DateTimeOffset end;
        if (!item.TryGetProperty("end", out var endEl) || !TryParseWhen(endEl, tz, out end, out _))
            end = allDay ? start.AddDays(1) : start;
        if (end < start) end = start;

        return new CalendarEvent(EventId(calendarId, id), (Str(item, "summary") ?? "").Trim(), start, end, allDay,
            NullIfEmpty(Str(item, "location")), NullIfEmpty(Str(item, "description")), SourceId(calendarId), colorHex);
    }

    /// <summary>{"date":"2026-10-13"} (all-day, local midnight) or {"dateTime":"…+02:00","timeZone":"…"}.</summary>
    private static bool TryParseWhen(JsonElement when, TimeZoneInfo tz, out DateTimeOffset value, out bool allDay)
    {
        value = default;
        allDay = false;
        if (when.ValueKind != JsonValueKind.Object) return false;
        var dateTime = Str(when, "dateTime");
        if (dateTime is not null)
        {
            if (!DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
            value = TimeZoneInfo.ConvertTime(parsed, tz);
            return true;
        }
        var date = Str(when, "date");
        if (date is not null && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            value = LocalMidnight(d, tz);
            allDay = true;
            return true;
        }
        return false;
    }

    /// <summary>JSON body for events.insert. All-day: dates with an exclusive end; timed: RFC 3339 + IANA/Windows zone.</summary>
    public static string BuildInsertBody(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay, TimeZoneInfo tz)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("summary", title);
            if (isAllDay)
            {
                var (startDate, endDate) = AllDayRange(start, end, tz);
                w.WriteStartObject("start");
                w.WriteString("date", startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteEndObject();
                w.WriteStartObject("end");
                w.WriteString("date", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteEndObject();
            }
            else
            {
                var zone = IanaZone(tz);
                WriteTimed(w, "start", TimeZoneInfo.ConvertTime(start, tz), zone);
                WriteTimed(w, "end", TimeZoneInfo.ConvertTime(end < start ? start : end, tz), zone);
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Local start date and exclusive end date of an all-day event (at least one day).</summary>
    public static (DateOnly Start, DateOnly EndExclusive) AllDayRange(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo tz)
    {
        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, tz).DateTime);
        var endLocal = TimeZoneInfo.ConvertTime(end < start ? start : end, tz);
        var endDate = DateOnly.FromDateTime(endLocal.DateTime);
        if (endLocal.TimeOfDay > TimeSpan.Zero || endDate <= startDate) endDate = endDate.AddDays(1);
        return (startDate, endDate);
    }

    private static void WriteTimed(Utf8JsonWriter w, string name, DateTimeOffset value, string? zone)
    {
        w.WriteStartObject(name);
        w.WriteString("dateTime", Rfc3339(value));
        if (zone is not null) w.WriteString("timeZone", zone);
        w.WriteEndObject();
    }

    private static string? IanaZone(TimeZoneInfo tz)
    {
        if (tz.HasIanaId) return tz.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out var iana) ? iana : null;
    }

    /// <summary>Error code of a Google API error body ({"error":{"status":"…"}} or {"error":"invalid_grant"}).</summary>
    public static string? ParseErrorCode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return null;
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            return error.ValueKind == JsonValueKind.Object ? Str(error, "status") ?? Str(error, "message") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static string NormalizeColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' ? color.ToUpperInvariant() : DefaultColor;

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
