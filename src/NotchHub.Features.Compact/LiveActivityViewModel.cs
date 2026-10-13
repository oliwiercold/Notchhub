using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Compact;

public sealed partial class LiveActivityViewModel : ObservableObject
{
    /// <summary>How long the live activity stays after playback pauses (avoids flicker between tracks).</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);
    /// <summary>An upcoming event this close wins over the ongoing one (time to get ready for the next meeting).</summary>
    private static readonly TimeSpan PreferUpcoming = TimeSpan.FromMinutes(10);
    /// <summary>Longest sleep while waiting for the next meeting-bar change (also picks up calendar syncs we missed).</summary>
    private static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(15);

    private readonly DispatcherTimer _grace = new() { Interval = Grace };
    /// <summary>One-shot meeting timer: next minute boundary of the countdown while visible, next transition while hidden.</summary>
    private readonly DispatcherTimer _meetingTimer = new(DispatcherPriority.Background);
    private CalendarEvent? _event;
    private bool _mediaActive;

    // preview overrides (tools/preview)
    private CalendarEvent? _previewEvent;
    private bool? _previewMedia;

    /// <summary>True when there is something to show around the collapsed notch (media and/or meeting bar). The shell observes it
    /// (PropertyChanged) to switch between the plain collapsed notch and the live-activity size; the view's desired width is the notch width.</summary>
    [ObservableProperty]
    private bool _hasActivity;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private byte[]? _thumbnail;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _isShown;

    // ── layout ──
    /// <summary>Album art + equalizer visible.</summary>
    [ObservableProperty] private bool _showMedia;
    /// <summary>Meeting text visible.</summary>
    [ObservableProperty] private bool _showMeeting;
    /// <summary>Meeting only (no media): calendar icon on the left.</summary>
    [ObservableProperty] private bool _showMeetingIcon;

    // ── meeting bar ──
    [ObservableProperty] private string _meetingWhen = "";
    [ObservableProperty] private string _meetingTitle = "";
    [ObservableProperty] private string _meetingTooltip = "";
    [ObservableProperty] private Brush? _meetingBrush;
    [ObservableProperty] private bool _isOngoing;
    [ObservableProperty] private double _meetingProgress;

    public LiveActivityViewModel()
    {
        _grace.Tick += (_, _) => { _grace.Stop(); if (!IsPlaying) { _mediaActive = false; Refresh(); } };
        _meetingTimer.Tick += (_, _) => { _meetingTimer.Stop(); UpdateMeeting(); };
        if (AppServices.Media is { } media)
        {
            // Event based (no polling) and always on: HasActivity drives the shell even while this view is hidden.
            media.Changed += OnMediaChanged;
            UpdateMedia();
        }
        if (AppServices.Calendar is { } cal) cal.Changed += OnMeetingSourceChanged;
        if (AppServices.Settings is { } settings) settings.Changed += OnMeetingSourceChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        UpdateMeeting();
    }

    private bool _released;
    private void OnMediaChanged(object? s, EventArgs e) => AppServices.Ui.Post(() => { if (!_released) UpdateMedia(); });
    private void OnMeetingSourceChanged(object? s, EventArgs e) => AppServices.Ui.Post(() => { if (!_released) UpdateMeeting(); });
    private void OnLanguageChanged(object? s, EventArgs e) { if (!_released) UpdateMeeting(); }

    /// <summary>Final release when the shell throws the view away: the always-on subscriptions and timers go too.</summary>
    public void Release()
    {
        _released = true;
        IsShown = false;
        if (AppServices.Media is { } media) media.Changed -= OnMediaChanged;
        if (AppServices.Calendar is { } cal) cal.Changed -= OnMeetingSourceChanged;
        if (AppServices.Settings is { } settings) settings.Changed -= OnMeetingSourceChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
        _grace.Stop();
        _meetingTimer.Stop();
        Thumbnail = null;
    }

    public void OnShown() { if (_released) return; IsShown = true; UpdateMeeting(); }
    public void OnHidden() { IsShown = false; UpdateMeeting(); }

    // ───────────────────────── media ─────────────────────────

    private void UpdateMedia()
    {
        var s = AppServices.Media?.Current;
        var playing = s?.IsPlaying == true;
        if (s is not null)
        {
            if (!ReferenceEquals(Thumbnail, s.Thumbnail)) Thumbnail = s.Thumbnail;
            Title = s.Title;
        }
        IsPlaying = playing;
        if (playing)
        {
            _grace.Stop();
            _mediaActive = true;
        }
        else if (_mediaActive && s is not null && s.State != MediaPlaybackState.Closed)
        {
            if (!_grace.IsEnabled) _grace.Start();
        }
        else
        {
            _grace.Stop();
            _mediaActive = false;
        }
        Refresh();
    }

    // ───────────────────────── meeting bar ─────────────────────────

    private void UpdateMeeting()
    {
        _meetingTimer.Stop();
        var now = DateTimeOffset.Now;
        var mb = AppServices.Settings?.Current.MeetingBar;
        CalendarEvent? ev = _previewEvent;
        var wake = now + MaxSleep;

        if (ev is null && mb is { Enabled: true } && AppServices.Calendar is { } cal)
        {
            IReadOnlyList<CalendarEvent> events;
            try { events = cal.GetEvents(now, now + TimeSpan.FromMinutes(Math.Max(1, mb.LeadMinutes)) + MaxSleep); }
            catch (Exception ex) { Log.Warn("Meeting bar: GetEvents failed", ex); events = []; }

            var lead = TimeSpan.FromMinutes(Math.Max(1, mb.LeadMinutes));
            var candidates = events.Where(e =>
                    !mb.ExcludedSources.Contains(e.SourceId) && !(mb.SkipAllDay && e.IsAllDay) && e.End > now)
                .ToList();
            var upcoming = candidates.Where(e => e.Start > now && e.Start - now <= lead).MinBy(e => e.Start);
            var ongoing = mb.ShowOngoing ? candidates.Where(e => e.Start <= now).MinBy(e => e.End) : null;
            ev = ongoing is not null && (upcoming is null || upcoming.Start - now > PreferUpcoming) ? ongoing : upcoming ?? ongoing;

            // next moment the choice can change: an event entering the lead window, starting, ending, or the "prefer upcoming" switch
            foreach (var e in candidates)
            {
                foreach (var t in new[] { e.Start - lead, e.Start - PreferUpcoming, e.Start, e.End })
                    if (t > now && t < wake) wake = t;
            }
        }

        _event = ev;
        if (ev is not null)
        {
            var ongoingNow = ev.Start <= now;
            var target = ongoingNow ? ev.End : ev.Start;
            var remaining = target - now;
            IsOngoing = ongoingNow;
            MeetingWhen = Loc.Format(ongoingNow ? "compact.meeting.left" : "compact.meeting.in", FormatSpan(remaining));
            var max = Math.Max(8, mb?.MaxTitleChars ?? 28);
            var title = string.IsNullOrWhiteSpace(ev.Title) ? "—" : ev.Title.Trim();
            if (title.Length > max) title = title[..(max - 1)].TrimEnd() + "…";
            MeetingTitle = ongoingNow ? title : $"{ev.Start.ToLocalTime().ToString("t", Loc.Culture)} {title}";
            MeetingTooltip = ev.Title;
            MeetingProgress = ongoingNow && ev.End > ev.Start ? Math.Clamp((now - ev.Start) / (ev.End - ev.Start), 0, 1) : 0;
            MeetingBrush = EventBrush(ev);

            if (IsShown || _previewEvent is not null)
            {
                // live countdown: tick when the displayed minute changes, every second in the last minute
                TimeSpan next;
                if (remaining <= TimeSpan.FromMinutes(1)) next = remaining - TimeSpan.FromSeconds(Math.Floor(remaining.TotalSeconds - 0.001));
                else
                {
                    var mins = Math.Ceiling(remaining.TotalMinutes);
                    next = remaining - TimeSpan.FromMinutes(mins - 1);
                }
                if (next <= TimeSpan.Zero) next = TimeSpan.FromSeconds(1);
                if (now + next < wake) wake = now + next;
            }
            else if (target < wake) wake = target;
        }
        Refresh();

        if (_previewEvent is not null) return; // preview renders a frozen frame
        var delay = wake - DateTimeOffset.Now;
        _meetingTimer.Interval = delay < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : delay + TimeSpan.FromMilliseconds(20);
        _meetingTimer.Start();
    }

    private static string FormatSpan(TimeSpan t)
    {
        if (t <= TimeSpan.FromMinutes(1)) return Loc.Format("compact.unit.sec", Math.Max(1, (int)Math.Ceiling(t.TotalSeconds)));
        var mins = (int)Math.Ceiling(t.TotalMinutes);
        if (mins < 60) return Loc.Format("compact.unit.min", mins);
        var h = mins / 60;
        var m = mins % 60;
        return m == 0 ? Loc.Format("compact.unit.hour", h) : Loc.Format("compact.unit.hourMin", h, m);
    }

    private static Brush? EventBrush(CalendarEvent ev)
    {
        var hex = ev.ColorHex;
        if (string.IsNullOrEmpty(hex))
        {
            try { hex = AppServices.Calendar?.Sources.FirstOrDefault(s => s.Id == ev.SourceId)?.ColorHex; }
            catch { hex = null; }
        }
        if (!string.IsNullOrEmpty(hex))
        {
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex));
                b.Freeze();
                return b;
            }
            catch { /* fall back to the theme accent */ }
        }
        return Application.Current?.TryFindResource("Brush.Primary") as Brush;
    }

    private void Refresh()
    {
        var media = _previewMedia ?? _mediaActive;
        var allowWithMedia = AppServices.Settings?.Current.MeetingBar.ShowWithMedia ?? true;
        var meeting = _event is not null && (!media || allowWithMedia);
        ShowMedia = media;
        ShowMeeting = meeting;
        ShowMeetingIcon = meeting && !media;
        HasActivity = media || meeting;
    }

    // ───────────────────────── preview ─────────────────────────

    internal void SetPreview(CalendarEvent? ev, bool media)
    {
        _previewEvent = ev;
        _previewMedia = media;
        UpdateMeeting();
    }
}
