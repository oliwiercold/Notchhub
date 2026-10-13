using System.Net.NetworkInformation;
using Ical.Net;
using Ical.Net.DataTypes;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Services.Common;
using IcalCalendar = Ical.Net.Calendar;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace NotchHub.Services.Calendar;

/// <summary>
/// Local events (calendar.json), ICS subscriptions from the settings and Google calendars (CalendarService.Google.cs). Subscriptions are downloaded every
/// <see cref="CalendarSettings.RefreshMinutes"/> (and when the subscription list changes), cached on disk for offline
/// start-up and expanded (RRULE/EXDATE/RECURRENCE-ID/TZID/all-day) for the window now −7 … +60 days.
/// </summary>
public sealed partial class CalendarService : ICalendarService
{
    internal const string LocalSourceId = "local";
    private const string LocalColor = "#8AB4F8";
    private static readonly TimeSpan PastWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan FutureWindow = TimeSpan.FromDays(60);
    private static readonly HttpClient SharedHttp = ServiceHttp.Create(TimeSpan.FromSeconds(30));

    private readonly ISettingsService _settings;
    private readonly DebouncedJsonWriter<LocalCalendarFile> _localWriter;
    private readonly string _cacheDir;
    private readonly Func<Uri, CancellationToken, Task<string>> _fetch;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly AsyncSignal _wake = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly List<CalendarEvent> _local;
    private readonly Dictionary<string, RemoteSource> _remote = new(StringComparer.Ordinal);
    private IReadOnlyList<CalendarSourceInfo> _sources = [];
    private string? _subscriptionsKey;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue; // wall clock of the last completed refresh
    private bool _started;
    private bool _disposed;

    public CalendarService(ISettingsService settings)
        : this(settings, AppPaths.File("calendar.json"), AppPaths.Dir("calendar-cache"), null, TimeProvider.System,
            Path.Combine(AppPaths.Dir("calendar"), "google-token.bin")) { }

    internal CalendarService(ISettingsService settings, string localPath, string cacheDir,
        Func<Uri, CancellationToken, Task<string>>? fetch, TimeProvider time, string? googleTokenPath = null)
    {
        _googleStore = new GoogleTokenStore(googleTokenPath ?? Path.Combine(cacheDir, "google-token.bin"));
        _settings = settings;
        _cacheDir = cacheDir;
        _time = time;
        _fetch = fetch ?? ((uri, ct) => SharedHttp.GetStringAsync(uri, ct));
        _localWriter = new DebouncedJsonWriter<LocalCalendarFile>(localPath, TimeSpan.FromMilliseconds(300));
        var file = JsonStore.Load(localPath, () => new LocalCalendarFile());
        _local = (file.Events ?? []).Where(e => e is not null && !string.IsNullOrEmpty(e.Id))
            .Select(e => e with { SourceId = LocalSourceId }).ToList();
        SyncSubscriptions();
        LoadGoogleToken();
        SyncGoogleSettings();
    }

    public IReadOnlyList<CalendarSourceInfo> Sources => Volatile.Read(ref _sources);

    public event EventHandler? Changed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started || _disposed) return Task.CompletedTask;
            _started = true;
        }
        _settings.Changed += OnSettingsChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        // Pick up settings changed between construction and start.
        SyncSubscriptions();
        SyncGoogleSettings();
        RunLoopAsync(_cts.Token).Forget("CalendarService.loop");
        return Task.CompletedTask;
    }

    public IReadOnlyList<CalendarEvent> GetEvents(DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<CalendarEvent>();
        lock (_gate)
        {
            result.AddRange(_local.Where(e => Overlaps(e, from, to)));
            foreach (var source in _remote.Values)
                if (source.Enabled) result.AddRange(source.Events.Where(e => Overlaps(e, from, to)));
            result.AddRange(GoogleEventsUnderLock().Where(e => Overlaps(e, from, to)));
        }
        result.Sort(CompareEvents);
        return result;
    }

    public CalendarEvent? GetNextEvent(DateTimeOffset now)
    {
        var tz = _time.LocalTimeZone;
        // Ongoing events and those starting within 7 days. Ordered by the local day they are relevant on
        // (today for ongoing ones), timed events before all-day events of the same day, then by start.
        return GetEvents(now, now.AddDays(7))
            .Where(e => e.End > now || (e.End == e.Start && e.Start >= now))
            .OrderBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Start > now ? e.Start : now, tz).DateTime))
            .ThenBy(e => e.IsAllDay)
            .ThenBy(e => e.Start)
            .ThenBy(e => e.Title, StringComparer.CurrentCulture)
            .FirstOrDefault();
    }

    public CalendarEvent AddLocalEvent(string title, DateTimeOffset start, DateTimeOffset end, bool isAllDay, string? colorHex = null)
    {
        if (end < start) end = start;
        if (isAllDay)
        {
            var tz = _time.LocalTimeZone;
            start = LocalMidnight(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, tz).DateTime), tz);
            var endLocal = TimeZoneInfo.ConvertTime(end, tz);
            var endDate = DateOnly.FromDateTime(endLocal.DateTime);
            if (endLocal.TimeOfDay > TimeSpan.Zero || endDate <= DateOnly.FromDateTime(start.DateTime)) endDate = endDate.AddDays(1);
            end = LocalMidnight(endDate, tz);
        }

        var ev = new CalendarEvent(Guid.NewGuid().ToString("N"), (title ?? "").Trim(), start, end, isAllDay,
            null, null, LocalSourceId, colorHex);
        lock (_gate)
        {
            _local.Add(ev);
            _localWriter.Schedule(new LocalCalendarFile { Events = [.. _local] });
        }
        RaiseChanged();
        return ev;
    }

    public void RemoveLocalEvent(string id)
    {
        lock (_gate)
        {
            if (_local.RemoveAll(e => e.Id == id) == 0) return;
            _localWriter.Schedule(new LocalCalendarFile { Events = [.. _local] });
        }
        RaiseChanged();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await RefreshCoreAsync(linked.Token).ConfigureAwait(false);
    }

    // ───────────────────────────── refresh loop ─────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            LoadDiskCaches();
            while (!ct.IsCancellationRequested)
            {
                await RefreshCoreAsync(ct).ConfigureAwait(false);
                // Wait until the refresh is due by the wall clock. Single waits are capped because timers do not advance
                // while the PC sleeps; a wake-up (settings / network change) refreshes right away.
                while (true)
                {
                    var minutes = Math.Max(5, _settings.Current.Calendar?.RefreshMinutes ?? 15);
                    DateTimeOffset last;
                    lock (_gate) last = _lastRefresh;
                    var due = TimeSpan.FromMinutes(minutes) - (_time.GetUtcNow() - last);
                    if (due <= TimeSpan.Zero) break;
                    if (await _wake.WaitAsync(due < MaxSleep ? due : MaxSleep, _time, ct).ConfigureAwait(false)) break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>Longest single wait of the refresh loop (sleep time is not counted by timers).</summary>
    private static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(5);

    /// <summary>Connectivity is back (e.g. after resume): refresh now instead of after the full interval.</summary>
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) _wake.Set();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var subscriptionsChanged = SyncSubscriptions();
        var googleChanged = SyncGoogleSettings();
        if (subscriptionsChanged || googleChanged)
        {
            RaiseChanged();
            _wake.Set();
        }
    }

    /// <summary>Copies the subscription list from the settings. True when it changed.</summary>
    private bool SyncSubscriptions()
    {
        var subs = (_settings.Current.Calendar?.Subscriptions ?? [])
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Id))
            .DistinctBy(s => s.Id)
            .Select(s => new RemoteSource(s.Id, s.Name ?? "", (s.Url ?? "").Trim(), string.IsNullOrWhiteSpace(s.ColorHex) ? LocalColor : s.ColorHex, s.Enabled))
            .ToList();
        var key = string.Join("\n", subs.Select(s => $"{s.Id}|{s.Url}|{s.Name}|{s.ColorHex}|{s.Enabled}"));
        lock (_gate)
        {
            if (key == _subscriptionsKey) return false;
            _subscriptionsKey = key;
            var old = new Dictionary<string, RemoteSource>(_remote);
            _remote.Clear();
            foreach (var s in subs)
            {
                if (old.TryGetValue(s.Id, out var previous) && previous.Url == s.Url)
                {
                    s.Ics = previous.Ics;
                    s.LastSync = previous.LastSync;
                    s.Error = previous.Error;
                    s.Events = Recolor(previous.Events, s.ColorHex);
                }
                _remote[s.Id] = s;
            }
            RebuildSources();
        }
        return true;
    }

    private void LoadDiskCaches()
    {
        List<RemoteSource> sources;
        lock (_gate) sources = _remote.Values.Where(s => s.Enabled && s.Ics is null).ToList();
        foreach (var source in sources)
        {
            try
            {
                var path = CachePath(source);
                if (!File.Exists(path)) continue;
                var ics = File.ReadAllText(path);
                ApplyIcs(source, ics, lastSync: null, error: null);
            }
            catch (Exception ex)
            {
                Log.Warn($"CalendarService: failed to load cached calendar {source.Id}", ex);
            }
        }
        RaiseChanged();
    }

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<RemoteSource> sources;
            lock (_gate) sources = _remote.Values.Where(s => s.Enabled).ToList();
            await Task.WhenAll(sources.Select(s => RefreshSourceAsync(s, ct)).Append(RefreshGoogleAsync(ct))).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _lastRefresh = _time.GetUtcNow();
                RebuildSources();
            }
            RaiseChanged();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshSourceAsync(RemoteSource source, CancellationToken ct)
    {
        if (!TryParseUrl(source.Url, out var uri))
        {
            SetError(source, "invalid_url");
            return;
        }

        string ics;
        try
        {
            ics = await _fetch(uri, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var code = ServiceHttp.ErrorCode(ex, ct);
            if (ServiceHttp.IsExpectedNetworkFailure(ex)) Log.Warn($"CalendarService: download of {source.Id} failed ({code})");
            else Log.Warn($"CalendarService: download of {source.Id} failed", ex);
            SetError(source, code);
            // Keep the window moving with the last known data.
            string? previous;
            lock (_gate) previous = source.Ics;
            if (previous is not null) ApplyIcs(source, previous, source.LastSync, code);
            return;
        }

        if (ApplyIcs(source, ics, _time.GetUtcNow(), null))
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                await File.WriteAllTextAsync(CachePath(source), ics, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"CalendarService: failed to cache calendar {source.Id}", ex);
            }
        }
    }

    /// <summary>Parses + expands <paramref name="ics"/> into the source. False (and error "parse") when invalid.</summary>
    private bool ApplyIcs(RemoteSource source, string ics, DateTimeOffset? lastSync, string? error)
    {
        var now = _time.GetUtcNow();
        IReadOnlyList<CalendarEvent> events;
        try
        {
            events = ExpandIcs(ics, source.Id, source.ColorHex, now - PastWindow, now + FutureWindow, _time.LocalTimeZone);
        }
        catch (Exception ex)
        {
            Log.Warn($"CalendarService: failed to parse calendar {source.Id}", ex);
            SetError(source, "parse");
            return false;
        }

        lock (_gate)
        {
            source.Ics = ics;
            source.Events = events;
            source.Error = error;
            if (lastSync is not null) source.LastSync = lastSync;
        }
        return true;
    }

    private void SetError(RemoteSource source, string code)
    {
        lock (_gate) source.Error = code;
    }

    private void RebuildSources()
    {
        var list = new List<CalendarSourceInfo> { new(LocalSourceId, "", LocalColor, true, null, null) };
        list.AddRange(_remote.Values.Where(s => s.Enabled)
            .Select(s => new CalendarSourceInfo(s.Id, s.Name, s.ColorHex, false, s.LastSync, s.Error)));
        Volatile.Write(ref _sources, list.ToArray());
    }

    private string CachePath(RemoteSource source)
    {
        var safe = string.Concat(source.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_cacheDir, safe + ".ics");
    }

    private static bool TryParseUrl(string url, out Uri uri)
    {
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url["webcal://".Length..];
        if (url.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url["webcals://".Length..];
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    // ───────────────────────────── ICS expansion ─────────────────────────────

    /// <summary>Expands all VEVENTs of <paramref name="ics"/> overlapping [from, to), sorted by start.</summary>
    internal static IReadOnlyList<CalendarEvent> ExpandIcs(string ics, string sourceId, string? colorHex,
        DateTimeOffset from, DateTimeOffset to, TimeZoneInfo tz)
    {
        var calendar = IcalCalendar.Load(ics) ?? throw new FormatException("Not an iCalendar document.");
        var result = new List<CalendarEvent>();
        // Start a day early so long/all-day events that began before the window are still seen.
        var start = new CalDateTime(from.UtcDateTime.AddDays(-1));
        var end = new CalDateTime(to.UtcDateTime);
        foreach (var occurrence in calendar.GetOccurrences<IcalEvent>(start, null).TakeWhileBefore(end).Take(20_000))
        {
            if (occurrence.Source is not IcalEvent ev) continue;
            if (string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;

            var period = occurrence.Period;
            var allDay = !period.StartTime.HasTime;
            var evStart = ToOffset(period.StartTime, tz);
            var endTime = period.EffectiveEndTime ?? period.EndTime;
            var evEnd = endTime is not null ? ToOffset(endTime, tz) : allDay ? evStart.AddDays(1) : evStart;
            if (evEnd < evStart) evEnd = evStart;

            var item = new CalendarEvent(
                $"{sourceId}:{ev.Uid}:{evStart.UtcTicks}",
                (ev.Summary ?? "").Trim(),
                evStart, evEnd, allDay,
                NullIfEmpty(ev.Location), NullIfEmpty(ev.Description),
                sourceId, colorHex);
            if (Overlaps(item, from, to)) result.Add(item);
        }
        result.Sort(CompareEvents);
        return result;
    }

    private static DateTimeOffset ToOffset(CalDateTime value, TimeZoneInfo tz)
    {
        if (!value.HasTime) return LocalMidnight(value.Date, tz);
        if (value.IsFloating)
        {
            var local = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, tz.GetUtcOffset(local));
        }
        var utc = new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc));
        return TimeZoneInfo.ConvertTime(utc, tz);
    }

    private static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static bool Overlaps(CalendarEvent e, DateTimeOffset from, DateTimeOffset to) =>
        e.End > e.Start ? e.Start < to && e.End > from : e.Start >= from && e.Start < to;

    private static int CompareEvents(CalendarEvent a, CalendarEvent b)
    {
        var c = a.Start.CompareTo(b.Start);
        if (c != 0) return c;
        c = b.IsAllDay.CompareTo(a.IsAllDay); // all-day first within the same start
        return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
    }

    private static IReadOnlyList<CalendarEvent> Recolor(IReadOnlyList<CalendarEvent> events, string color) =>
        events.Count == 0 || events[0].ColorHex == color ? events : events.Select(e => e with { ColorHex = color }).ToArray();

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void RaiseChanged() => SafeEvents.Raise(Changed, this, "CalendarService.Changed");

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _settings.Changed -= OnSettingsChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _cts.Cancel();
        try { _connectCts?.Cancel(); } catch (ObjectDisposedException) { }
        _localWriter.Flush();
    }

    private sealed class RemoteSource(string id, string name, string url, string colorHex, bool enabled)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string Url { get; } = url;
        public string ColorHex { get; } = colorHex;
        public bool Enabled { get; } = enabled;
        public string? Ics { get; set; }
        public IReadOnlyList<CalendarEvent> Events { get; set; } = [];
        public DateTimeOffset? LastSync { get; set; }
        public string? Error { get; set; }
    }

    internal sealed class LocalCalendarFile
    {
        public int Version { get; set; } = 1;
        public List<CalendarEvent>? Events { get; set; } = [];
    }
}
