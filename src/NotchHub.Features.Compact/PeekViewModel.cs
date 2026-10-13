using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Converters;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Compact;

public sealed partial class PeekViewModel : ObservableObject
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private MediaSessionInfo? _session;
    private bool _shown;

    /// <summary>True when the peek shows the mini player; false = glance card (time/weather/phone).</summary>
    [ObservableProperty]
    private bool _showsMedia;

    /// <summary>Glance card on the left + mini player on the right (settings Notch.PeekGlanceWithMedia and media present).</summary>
    [ObservableProperty] private bool _isCombined;
    /// <summary>Full-size glance card only (no media).</summary>
    [ObservableProperty] private bool _showsFullGlance = true;

    // ── mini player ──
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private byte[]? _thumbnail;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "";
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private bool _canPrevious;

    // ── glance ──
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private bool _hasWeather;
    [ObservableProperty] private string _weatherGlyph = "cloud";
    [ObservableProperty] private string _temperatureText = "";
    [ObservableProperty] private string _nextEventText = "";
    [ObservableProperty] private bool _hasPhone;
    [ObservableProperty] private string _phoneBatteryText = "";
    [ObservableProperty] private string _phoneBatteryGlyph = "battery_full";
    [ObservableProperty] private int _phoneNotificationCount;

    public PeekViewModel()
    {
        _tick.Tick += (_, _) => Tick();
        if (AppServices.Media is { } media)
        {
            // Always on (event based) so ShowsMedia is right when the shell measures the peek before showing it.
            media.Changed += OnMediaChanged;
            UpdateMedia();
        }
        if (AppServices.Settings is { } settings) settings.Changed += OnSettingsChanged;
        UpdateClock();
        UpdateLayoutFlags();
    }

    private bool _released;
    private void OnMediaChanged(object? s, EventArgs e) => AppServices.Ui.Post(() => { if (!_released) UpdateMedia(); });
    private void OnSettingsChanged(object? s, EventArgs e) => AppServices.Ui.Post(() => { if (!_released) UpdateLayoutFlags(); });

    /// <summary>Final release when the shell throws the view away: the always-on subscriptions go too.</summary>
    public void Release()
    {
        OnHidden();
        _released = true;
        if (AppServices.Media is { } media) media.Changed -= OnMediaChanged;
        if (AppServices.Settings is { } settings) settings.Changed -= OnSettingsChanged;
        _session = null;
        Thumbnail = null;
    }

    partial void OnShowsMediaChanged(bool value) => UpdateLayoutFlags();

    private void UpdateLayoutFlags()
    {
        IsCombined = ShowsMedia && (AppServices.Settings?.Current.Notch.PeekGlanceWithMedia ?? true);
        ShowsFullGlance = !ShowsMedia;
    }

    public void OnShown()
    {
        if (_shown || _released) return;
        _shown = true;
        if (AppServices.Weather is { } w) w.Changed += OnGlanceChanged;
        if (AppServices.Calendar is { } c) c.Changed += OnGlanceChanged;
        if (AppServices.Phone is { } p) p.Changed += OnGlanceChanged;
        UpdateMedia();
        UpdateGlance();
        _tick.Start();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        _tick.Stop();
        if (AppServices.Weather is { } w) w.Changed -= OnGlanceChanged;
        if (AppServices.Calendar is { } c) c.Changed -= OnGlanceChanged;
        if (AppServices.Phone is { } p) p.Changed -= OnGlanceChanged;
    }

    private void OnGlanceChanged(object? sender, EventArgs e) => AppServices.Ui.Post(() => { if (_shown) UpdateGlance(); });

    private void UpdateMedia()
    {
        var s = AppServices.Media?.Current;
        _session = s;
        ShowsMedia = s is not null && s.State != MediaPlaybackState.Closed;
        if (s is null) { IsPlaying = false; return; }
        Title = s.Title;
        Artist = string.IsNullOrEmpty(s.Artist) ? s.SourceAppName : s.Artist;
        if (!ReferenceEquals(Thumbnail, s.Thumbnail)) Thumbnail = s.Thumbnail;
        IsPlaying = s.IsPlaying;
        CanSeek = s.CanSeek && s.Duration > TimeSpan.Zero;
        CanNext = s.CanNext;
        CanPrevious = s.CanPrevious;
        DurationText = s.Duration > TimeSpan.Zero ? TimeSpanClock.ToClock(s.Duration) : "";
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        var s = _session;
        if (s is null) return;
        var pos = s.GetLivePosition(DateTimeOffset.Now);
        PositionText = TimeSpanClock.ToClock(pos);
        Progress = s.Duration > TimeSpan.Zero ? Math.Clamp(pos / s.Duration, 0, 1) : 0;
    }

    private void Tick()
    {
        if (ShowsMedia && IsPlaying) UpdatePosition();
        if (!ShowsMedia || IsCombined) UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var c = Loc.Culture;
        TimeText = now.ToString(c.TwoLetterISOLanguageName == "pl" ? "HH:mm" : "h:mm", c);
        var date = now.ToString("dddd, d MMMM", c);
        DateText = date.Length > 0 ? char.ToUpper(date[0], c) + date[1..] : date;
    }

    private void UpdateGlance()
    {
        UpdateClock();
        var w = AppServices.Weather?.Current;
        HasWeather = w is not null;
        if (w is not null)
        {
            WeatherGlyph = Glyphs.Weather(w.WeatherCode, w.IsDay);
            TemperatureText = $"{Math.Round(w.TemperatureC):0}°";
        }

        var now = DateTimeOffset.Now;
        var ev = SafeNextEvent(now);
        if (ev is null) NextEventText = Loc.Get("compact.noEvents");
        else
        {
            var c = Loc.Culture;
            var local = ev.Start.ToLocalTime();
            string when = ev.IsAllDay
                ? (local.Date == now.Date ? Loc.Get("compact.today") : local.ToString("d MMM", c))
                : local.Date == now.Date ? local.ToString("HH:mm", c)
                : local.Date == now.Date.AddDays(1) ? $"{Loc.Get("compact.tomorrow")} {local.ToString("HH:mm", c)}"
                : local.ToString("d MMM HH:mm", c);
            NextEventText = $"{when} · {ev.Title}";
        }

        var phone = AppServices.Phone;
        var bat = phone?.Battery;
        HasPhone = phone?.ActiveDevice is { PairState: PhonePairState.Paired } && bat is not null;
        if (HasPhone && bat is not null)
        {
            PhoneBatteryText = $"{bat.Percent}%";
            PhoneBatteryGlyph = Glyphs.Battery(bat.Percent, bat.IsCharging);
            PhoneNotificationCount = phone!.Notifications.Count;
        }
    }

    private static CalendarEvent? SafeNextEvent(DateTimeOffset now)
    {
        try { return AppServices.Calendar?.GetNextEvent(now); }
        catch (Exception ex) { Log.Warn("Peek: next event failed", ex); return null; }
    }

    [RelayCommand] private void PlayPause() => AppServices.Media.PlayPauseAsync().Forget("peek play/pause");
    [RelayCommand] private void Next() => AppServices.Media.NextAsync().Forget("peek next");
    [RelayCommand] private void Previous() => AppServices.Media.PreviousAsync().Forget("peek previous");

    public void Seek(double fraction)
    {
        var s = _session;
        if (s is null || s.Duration <= TimeSpan.Zero) return;
        var pos = TimeSpan.FromTicks((long)(s.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        Progress = fraction;
        PositionText = TimeSpanClock.ToClock(pos);
        AppServices.Media.SeekAsync(pos).Forget("peek seek");
    }
}
