using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.UI.Converters;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Today;

public sealed partial class TodayViewModel : ObservableObject
{
    private static readonly int[] PresetMinutes = [1, 5, 10, 15, 20];
    private static readonly int[] DurationMinutes = [15, 30, 60, 90];
    private static readonly string?[] SwatchColors = [null, "#F5C26B", "#8BD3A0", "#7FB8F0", "#B79CF0", "#F0907F"];

    private DispatcherTimer? _clockTimer;
    private bool _shown;
    private DateOnly _today;
    private IDisposable? _sheetHold;

    public TodayViewModel()
    {
        foreach (var m in PresetMinutes) Presets.Add(new Choice<int>($"{m}m", m));
        foreach (var m in DurationMinutes) Durations.Add(new Choice<int>($"{m} min", m));
        foreach (var c in SwatchColors) Swatches.Add(new Choice<string?>(c ?? "", c, BrushUtil.FromHex(c)));
        _timerMinutes = 5;
        UpdatePresetSelection();
    }

    // ───────────────────────────── lifecycle ─────────────────────────────

    public void OnShown()
    {
        if (_shown) return;
        _shown = true;
        AppServices.Weather.Changed += OnWeatherChanged;
        AppServices.Calendar.Changed += OnCalendarChanged;
        AppServices.Todos.Changed += OnTodosChanged;
        AppServices.Timer.Changed += OnTimerChanged;
        AppServices.Settings.Changed += OnSettingsChanged;

        _clockTimer ??= new DispatcherTimer(DispatcherPriority.Background);
        _clockTimer.Tick += OnClockTick;
        UpdateClock();
        RefreshWeek();
        RefreshWeather();
        RefreshTodos();
        RefreshTimer();
        ArmClock();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        AppServices.Weather.Changed -= OnWeatherChanged;
        AppServices.Calendar.Changed -= OnCalendarChanged;
        AppServices.Todos.Changed -= OnTodosChanged;
        AppServices.Timer.Changed -= OnTimerChanged;
        AppServices.Settings.Changed -= OnSettingsChanged;
        if (_clockTimer is not null)
        {
            _clockTimer.Stop();
            _clockTimer.Tick -= OnClockTick;
        }
        TimerIsWavy = false;
        IsDayExpanded = false; // reopen on the compact next-event card
    }

    private void OnWeatherChanged(object? s, EventArgs e) => AppServices.Ui.Post(RefreshWeather);
    private void OnCalendarChanged(object? s, EventArgs e) => AppServices.Ui.Post(RefreshWeek);
    private void OnTodosChanged(object? s, EventArgs e) => AppServices.Ui.Post(RefreshTodos);
    private void OnTimerChanged(object? s, EventArgs e) => AppServices.Ui.Post(RefreshTimer);
    private void OnSettingsChanged(object? s, EventArgs e) => AppServices.Ui.Post(() =>
    {
        if (!_shown) return;
        UpdateClock();
        RefreshWeek();
        RefreshWeather();
    });

    private static bool English => Loc.Language == "en";
    private static CultureInfo Culture => Loc.Culture;
    private static string TimeFormat => English ? "h:mm tt" : "HH:mm";
    private static string FormatTime(DateTimeOffset t) => t.ToLocalTime().ToString(TimeFormat, Culture);

    // ───────────────────────────── clock ─────────────────────────────

    [ObservableProperty] private string _clockHours = "";
    [ObservableProperty] private string _clockMinutes = "";
    [ObservableProperty] private string _clockDate = "";

    private void ArmClock()
    {
        if (_clockTimer is null || !_shown) return;
        var now = DateTime.Now;
        var next = now.AddSeconds(60 - now.Second).AddMilliseconds(-now.Millisecond + 30);
        _clockTimer.Interval = next - now;
        _clockTimer.Start();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        _clockTimer?.Stop();
        UpdateClock();
        if (DateOnly.FromDateTime(DateTime.Now) != _today) RefreshWeek();
        else RefreshEvents();
        ArmClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var c = Culture;
        ClockMinutes = now.ToString("mm", c);
        if (English)
        {
            ClockHours = now.ToString("hh", c);
            ClockDate = $"{now.ToString("tt", CultureInfo.GetCultureInfo("en-US"))} • {now.ToString("ddd d", c)}";
        }
        else
        {
            ClockHours = now.ToString("HH", c);
            ClockDate = $"{now.ToString("ddd", c)} • {now.ToString("d MMM", c)}";
        }
    }

    // ───────────────────────────── weather ─────────────────────────────

    [ObservableProperty] private bool _weatherDisabled;
    [ObservableProperty] private bool _weatherLoading;
    [ObservableProperty] private bool _weatherHasData;
    [ObservableProperty] private bool _weatherError;
    [ObservableProperty] private string _weatherIcon = "cloud";
    [ObservableProperty] private bool _weatherIsSun;
    [ObservableProperty] private string _weatherTemp = "";
    [ObservableProperty] private string _weatherDescription = "";
    [ObservableProperty] private string _weatherMinMax = "";
    [ObservableProperty] private string _weatherLocation = "";

    private void RefreshWeather()
    {
        var w = AppServices.Weather;
        var settings = AppServices.Settings.Current.Weather;
        WeatherDisabled = !settings.Enabled;
        WeatherLoading = w.IsLoading;
        var snap = w.Current;
        WeatherHasData = settings.Enabled && snap is not null;
        WeatherError = settings.Enabled && snap is null && !w.IsLoading;
        if (snap is null) return;
        var f = settings.Unit == TemperatureUnit.Fahrenheit;
        string T(double c) => Math.Round(f ? c * 9 / 5 + 32 : c).ToString("0", Culture) + "°";
        WeatherIcon = WeatherCodes.Icon(snap.WeatherCode, snap.IsDay);
        WeatherIsSun = WeatherCodes.IsSun(snap.WeatherCode, snap.IsDay);
        WeatherTemp = T(snap.TemperatureC) + (f ? "F" : "C");
        WeatherDescription = Loc.Get(WeatherCodes.DescriptionKey(snap.WeatherCode));
        WeatherMinMax = $"↑ {T(snap.TodayMaxC)}  ↓ {T(snap.TodayMinC)}";
        WeatherLocation = string.IsNullOrWhiteSpace(snap.LocationName) ? settings.LocationName ?? "" : snap.LocationName;
    }

    [RelayCommand]
    private void RefreshWeatherNow()
    {
        if (WeatherDisabled) return;
        WeatherLoading = true;
        WeatherError = false;
        AppServices.Weather.RefreshAsync().Forget("Today.Weather.Refresh");
    }

    // ───────────────────────────── week + events ─────────────────────────────

    public ObservableCollection<DayItem> Days { get; } = [];
    public ObservableCollection<EventItem> Events { get; } = [];
    [ObservableProperty] private string _weekRange = "";
    [ObservableProperty] private bool _hasNoEvents;
    private DateOnly _selected;

    private void RefreshWeek()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_selected == default || _selected == _today) _selected = today;
        _today = today;
        var mondayFirst = AppServices.Settings.Current.Calendar.WeekStartsMonday;
        var first = (DayOfWeek)(mondayFirst ? 1 : 0);
        var start = today.AddDays(-(((int)today.DayOfWeek - (int)first + 7) % 7));
        if (_selected < start || _selected > start.AddDays(6)) _selected = today;

        var from = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue));
        var events = AppServices.Calendar.GetEvents(from, from.AddDays(7));
        var names = Culture.DateTimeFormat.AbbreviatedDayNames;
        Days.Clear();
        for (var i = 0; i < 7; i++)
        {
            var d = start.AddDays(i);
            var dStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue));
            var dEnd = dStart.AddDays(1);
            var n = names[(int)d.DayOfWeek].TrimEnd('.');
            Days.Add(new DayItem
            {
                Date = d,
                Name = n.Length > 0 ? char.ToUpper(n[0], Culture) + n[1..] : n,
                IsToday = d == today,
                IsSelected = d == _selected && d != today,
                HasEvents = events.Any(e => e.Start < dEnd && e.End > dStart),
            });
        }
        var fmt = Loc.Get("today.dayMonthFormat");
        WeekRange = $"{start.ToString(fmt, Culture)} – {start.AddDays(6).ToString(fmt, Culture)}";
        RefreshEvents();
    }

    [RelayCommand]
    private void SelectDay(DayItem? day)
    {
        if (day is not null) OnDayClicked(day);
    }

    private static EventItem ToItem(CalendarEvent e, string header) => new()
    {
        Id = e.Id,
        Header = header,
        Title = e.Title,
        IsLocal = e.SourceId == "local",
        CanDelete = e.SourceId == "local" || IsWritableGoogle(e.SourceId),
        ColorBrush = BrushUtil.FromHex(e.ColorHex),
    };

    private static bool IsWritableGoogle(string sourceId) =>
        sourceId.StartsWith("google:", StringComparison.Ordinal)
        && AppServices.Calendar.GoogleCalendars.Any(c => c.CanWrite && sourceId.Length == 7 + c.Id.Length && sourceId.EndsWith(c.Id, StringComparison.Ordinal));

    private void RefreshEvents()
    {
        Events.Clear();
        var now = DateTimeOffset.Now;
        if (_selected == _today)
        {
            var next = AppServices.Calendar.GetNextEvent(now);
            if (next is not null)
            {
                string header;
                if (next.Start <= now && next.End > now)
                    header = $"{Loc.Get("today.nowLabel")} • {Range(next)}";
                else
                {
                    var day = DateOnly.FromDateTime(next.Start.LocalDateTime);
                    var when = next.IsAllDay ? Loc.Get("today.allDay") : FormatTime(next.Start);
                    if (day == _today.AddDays(1)) when = $"{Loc.Get("today.tomorrow")} {when}";
                    else if (day != _today) when = $"{next.Start.LocalDateTime.ToString("ddd", Culture)} {when}";
                    header = $"{Loc.Get("today.next")} • {when}";
                }
                Events.Add(ToItem(next, header));
            }
        }
        else
        {
            var from = new DateTimeOffset(_selected.ToDateTime(TimeOnly.MinValue));
            foreach (var e in AppServices.Calendar.GetEvents(from, from.AddDays(1)))
                Events.Add(ToItem(e, Range(e)));
        }
        HasNoEvents = Events.Count == 0;
        RefreshDayList();
    }

    private static string Range(CalendarEvent e) =>
        e.IsAllDay ? Loc.Get("today.allDay") : $"{FormatTime(e.Start)}–{FormatTime(e.End)}";

    // ───────────────────────────── add event sheet ─────────────────────────────

    public ObservableCollection<Choice<int>> Durations { get; } = [];
    public ObservableCollection<Choice<string?>> Swatches { get; } = [];
    /// <summary>Target calendars: "local" + writable, enabled Google calendars.</summary>
    public ObservableCollection<Choice<string>> Targets { get; } = [];
    public bool HasTargets => Targets.Count > 1;
    [ObservableProperty] private bool _isLocalTarget = true;
    [ObservableProperty] private string? _eventSaveError;
    [ObservableProperty] private bool _isEventSheetOpen;
    [ObservableProperty] private string _newTitle = "";
    [ObservableProperty] private int _newDayOffset;
    [ObservableProperty] private int _newHour;
    [ObservableProperty] private int _newMinute;
    public bool NewIsToday => NewDayOffset == 0;
    public bool NewIsTomorrow => NewDayOffset == 1;
    public string NewDateText => DateTime.Today.AddDays(NewDayOffset).ToString(English ? "ddd, MMM d" : "ddd, d MMM", Culture);
    public string NewTimeText => new DateTime(2000, 1, 1, NewHour, NewMinute, 0).ToString(TimeFormat, Culture);
    public bool CanSaveEvent => !string.IsNullOrWhiteSpace(NewTitle);

    partial void OnNewTitleChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveEvent));
        SaveEventCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewDayOffsetChanged(int value) => NotifyDate();
    partial void OnNewHourChanged(int value) => OnPropertyChanged(nameof(NewTimeText));
    partial void OnNewMinuteChanged(int value) => OnPropertyChanged(nameof(NewTimeText));

    private void NotifyDate()
    {
        OnPropertyChanged(nameof(NewIsToday));
        OnPropertyChanged(nameof(NewIsTomorrow));
        OnPropertyChanged(nameof(NewDateText));
    }

    partial void OnIsEventSheetOpenChanged(bool value)
    {
        _sheetHold?.Dispose();
        _sheetHold = value ? AppServices.Shell?.HoldOpen() : null;
    }

    [RelayCommand]
    private void OpenEventSheet()
    {
        var now = DateTime.Now.AddHours(1);
        NewTitle = "";
        NewDayOffset = Math.Max(0, _selected.DayNumber - _today.DayNumber);
        if (_selected == default) NewDayOffset = 0;
        NewHour = now.Hour;
        NewMinute = 0;
        SelectDuration(Durations[2]);
        SelectSwatch(Swatches[0]);
        LoadTargets();
        EventSaveError = null;
        NotifyDate();
        IsEventSheetOpen = true;
    }

    [RelayCommand] private void SetNewDay(string offset) { NewDayOffset = int.Parse(offset, CultureInfo.InvariantCulture); NotifyDate(); }
    [RelayCommand] private void ShiftNewDay(string delta) { NewDayOffset = Math.Max(0, NewDayOffset + int.Parse(delta, CultureInfo.InvariantCulture)); NotifyDate(); }
    [RelayCommand] private void ShiftHour(string delta) => NewHour = (NewHour + int.Parse(delta, CultureInfo.InvariantCulture) + 24) % 24;
    [RelayCommand] private void ShiftMinute(string delta) => NewMinute = (NewMinute + int.Parse(delta, CultureInfo.InvariantCulture) + 60) % 60;

    [RelayCommand]
    private void SelectDuration(Choice<int>? c)
    {
        foreach (var d in Durations) d.IsSelected = d == c;
    }

    [RelayCommand]
    private void SelectSwatch(Choice<string?>? c)
    {
        foreach (var s in Swatches) s.IsSelected = s == c;
    }

    private void LoadTargets()
    {
        Targets.Clear();
        Targets.Add(new Choice<string>(Loc.Get("today.event.local"), "local", BrushUtil.FromHex("#8AB4F8")));
        try
        {
            foreach (var c in AppServices.Calendar.GoogleCalendars.Where(c => c.CanWrite && c.IsEnabled))
                Targets.Add(new Choice<string>(c.Name, c.Id, BrushUtil.FromHex(c.ColorHex)));
        }
        catch (Exception ex)
        {
            Log.Warn("Today: reading Google calendars failed", ex);
        }
        var preferred = AppServices.Settings.Current.Calendar?.DefaultEventTarget ?? "local";
        SelectTarget(Targets.FirstOrDefault(t => t.Value == preferred) ?? Targets[0]);
        OnPropertyChanged(nameof(HasTargets));
    }

    [RelayCommand]
    private void SelectTarget(Choice<string>? c)
    {
        if (c is null) return;
        foreach (var t in Targets) t.IsSelected = t == c;
        IsLocalTarget = c.Value == "local";
    }

    [RelayCommand(CanExecute = nameof(CanSaveEvent))]
    private async Task SaveEvent()
    {
        var day = DateTime.Today.AddDays(NewDayOffset);
        var start = new DateTimeOffset(day.Year, day.Month, day.Day, NewHour, NewMinute, 0, TimeZoneInfo.Local.GetUtcOffset(day));
        var minutes = Durations.FirstOrDefault(d => d.IsSelected)?.Value ?? 60;
        var color = Swatches.FirstOrDefault(s => s.IsSelected)?.Value;
        var target = Targets.FirstOrDefault(t => t.IsSelected)?.Value ?? "local";
        EventSaveError = null;
        CalendarEvent? created = null;
        try
        {
            created = await AppServices.Calendar.AddEventAsync(NewTitle.Trim(), start, start.AddMinutes(minutes), false,
                target == "local" ? null : target, target == "local" ? color : null);
        }
        catch (Exception ex)
        {
            Log.Warn("Today: adding an event failed", ex);
        }
        if (created is null)
        {
            EventSaveError = Loc.Get("today.event.saveFailed");
            return;
        }
        IsEventSheetOpen = false;
        RefreshWeek();
    }

    [RelayCommand] private void CancelEvent() => IsEventSheetOpen = false;

    // ───────────────────────────── timer ─────────────────────────────

    public ObservableCollection<Choice<int>> Presets { get; } = [];
    [ObservableProperty] private int _timerHours;
    [ObservableProperty] private int _timerMinutes;
    [ObservableProperty] private double _timerValue = 1;
    [ObservableProperty] private string _timerText = "5:00";
    [ObservableProperty] private bool _timerIsWavy;
    [ObservableProperty] private bool _timerIsIdle = true;
    [ObservableProperty] private bool _timerIsActive;
    [ObservableProperty] private string _timerPrimaryLabel = "";
    [ObservableProperty] private string _timerPrimaryIcon = "play_arrow";

    private TimeSpan SetDuration => new(TimerHours, TimerMinutes, 0);

    partial void OnTimerHoursChanged(int value)
    {
        if (value is < 0 or > 23) { TimerHours = Math.Clamp(value, 0, 23); return; }
        UpdatePresetSelection();
        if (TimerIsIdle) RefreshTimer();
    }

    partial void OnTimerMinutesChanged(int value)
    {
        if (value is < 0 or > 59) { TimerMinutes = Math.Clamp(value, 0, 59); return; }
        UpdatePresetSelection();
        if (TimerIsIdle) RefreshTimer();
    }

    private void UpdatePresetSelection()
    {
        var total = TimerHours * 60 + TimerMinutes;
        foreach (var p in Presets) p.IsSelected = p.Value == total;
    }

    /// <summary>Wheel / arrow keys on the h/min fields.</summary>
    public void AdjustTimerField(bool hours, int delta)
    {
        if (!TimerIsIdle) return;
        if (hours) TimerHours = Math.Clamp(TimerHours + delta, 0, 23);
        else
        {
            var total = Math.Clamp(TimerHours * 60 + TimerMinutes + delta, 0, 23 * 60 + 59);
            TimerHours = total / 60;
            TimerMinutes = total % 60;
        }
    }

    [RelayCommand]
    private void SelectPreset(Choice<int>? c)
    {
        if (c is null || !TimerIsIdle) { UpdatePresetSelection(); return; }
        TimerHours = 0;
        TimerMinutes = c.Value;
        UpdatePresetSelection();
    }

    [RelayCommand]
    private void TimerPrimary()
    {
        var t = AppServices.Timer;
        switch (t.State)
        {
            case TimerState.Running: t.Pause(); break;
            case TimerState.Paused: t.Resume(); break;
            default:
                if (SetDuration > TimeSpan.Zero) t.Start(SetDuration);
                break;
        }
        RefreshTimer();
    }

    [RelayCommand] private void TimerReset() { AppServices.Timer.Reset(); RefreshTimer(); }
    [RelayCommand] private void TimerAddMinute() { AppServices.Timer.AddTime(TimeSpan.FromMinutes(1)); RefreshTimer(); }

    private void RefreshTimer()
    {
        var t = AppServices.Timer;
        var state = t.State;
        TimerIsIdle = state is TimerState.Idle or TimerState.Finished;
        TimerIsActive = !TimerIsIdle;
        TimerIsWavy = _shown && state == TimerState.Running;
        if (state == TimerState.Idle)
        {
            TimerValue = 1;
            TimerText = TimeSpanClock.ToClock(SetDuration);
        }
        else
        {
            var rem = t.Remaining < TimeSpan.Zero ? TimeSpan.Zero : t.Remaining;
            TimerValue = t.Duration > TimeSpan.Zero ? Math.Clamp(rem / t.Duration, 0, 1) : 0;
            TimerText = TimeSpanClock.ToClock(TimeSpan.FromSeconds(Math.Ceiling(rem.TotalSeconds)));
        }
        (TimerPrimaryLabel, TimerPrimaryIcon) = state switch
        {
            TimerState.Running => (Loc.Get("today.timer.pause"), "pause"),
            TimerState.Paused => (Loc.Get("today.timer.resume"), "play_arrow"),
            _ => (Loc.Get("today.timer.start"), "play_arrow"),
        };
    }

    // ───────────────────────────── to-do ─────────────────────────────

    public ObservableCollection<TodoVm> Todos { get; } = [];
    private readonly Dictionary<Guid, TodoVm> _todoMap = [];
    [ObservableProperty] private string _newTodoText = "";
    [ObservableProperty] private double _todoProgress;
    [ObservableProperty] private string _todoProgressText = "0/0";
    [ObservableProperty] private bool _hasNoTodos = true;

    private void RefreshTodos()
    {
        var items = AppServices.Todos.Items;
        var sameOrder = items.Count == Todos.Count && items.Select(i => i.Id).SequenceEqual(Todos.Select(t => t.Id));
        var vms = new List<TodoVm>(items.Count);
        foreach (var i in items)
        {
            if (!_todoMap.TryGetValue(i.Id, out var vm)) _todoMap[i.Id] = vm = new TodoVm(i.Id);
            vm.Sync(i.Text, i.IsDone);
            vms.Add(vm);
        }
        foreach (var gone in _todoMap.Keys.Except(items.Select(i => i.Id)).ToList()) _todoMap.Remove(gone);
        if (!sameOrder)
        {
            Todos.Clear();
            foreach (var vm in vms) Todos.Add(vm);
        }
        var done = items.Count(i => i.IsDone);
        TodoProgress = items.Count == 0 ? 0 : (double)done / items.Count;
        TodoProgressText = $"{done}/{items.Count}";
        HasNoTodos = items.Count == 0;
    }

    [RelayCommand]
    private void AddTodo()
    {
        var t = NewTodoText.Trim();
        if (t.Length == 0) return;
        AppServices.Todos.Add(t);
        NewTodoText = "";
        RefreshTodos();
    }
}
