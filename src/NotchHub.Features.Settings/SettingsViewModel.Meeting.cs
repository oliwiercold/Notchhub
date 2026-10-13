using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NotchHub.Core;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Settings;

/// <summary>Settings → Meeting bar, plus the newer Notch / Phone switches (peek glance, charging HUD, call status, OTP copy).</summary>
public sealed partial class SettingsViewModel
{
    // ═════════════ Pasek spotkań ═════════════

    [ObservableProperty] private bool _meetingEnabled;
    [ObservableProperty] private double _meetingLead;
    [ObservableProperty] private bool _meetingShowOngoing;
    [ObservableProperty] private bool _meetingWithMedia;
    [ObservableProperty] private bool _meetingSkipAllDay;
    [ObservableProperty] private double _meetingMaxTitle;
    public ObservableCollection<MeetingCalendarItem> MeetingCalendars { get; } = [];
    private string? _meetingCalendarsKey;

    public string MeetingLeadText => MeetingLead >= 120 && MeetingLead % 60 == 0
        ? Loc.Format("settings.Meeting.Hours", MeetingLead / 60)
        : Loc.Format("settings.Meeting.Minutes", MeetingLead);

    public string MeetingMaxTitleText => Loc.Format("settings.Meeting.Chars", MeetingMaxTitle);

    // ═════════════ Notch / Telefon (nowe przełączniki) ═════════════

    [ObservableProperty] private bool _peekGlanceWithMedia;
    [ObservableProperty] private bool _phoneChargingHud;
    [ObservableProperty] private bool _phoneCallStatus;
    [ObservableProperty] private bool _phoneOtpCopy;

    private void LoadExtras()
    {
        var m = S.MeetingBar;
        MeetingEnabled = m.Enabled;
        MeetingLead = Math.Clamp(m.LeadMinutes, 5, 180);
        MeetingShowOngoing = m.ShowOngoing;
        MeetingWithMedia = m.ShowWithMedia;
        MeetingSkipAllDay = m.SkipAllDay;
        MeetingMaxTitle = Math.Clamp(m.MaxTitleChars, 12, 60);
        _meetingCalendarsKey = null;
        LoadMeetingCalendars();

        PeekGlanceWithMedia = S.Notch.PeekGlanceWithMedia;
        PhoneChargingHud = S.Phone.ChargingHud;
        PhoneCallStatus = S.Phone.CallStatus;
        PhoneOtpCopy = S.Phone.OtpCopy;
    }

    /// <summary>Checklist of every calendar (local, ICS subscriptions, enabled Google calendars) → MeetingBar.ExcludedSources.</summary>
    private void LoadMeetingCalendars()
    {
        var list = new List<(string Id, string Name, string Color, string Kind)>();
        try
        {
            var cal = AppServices.Calendar;
            foreach (var s in cal?.Sources ?? [])
            {
                if (s.Id.StartsWith("google:", StringComparison.Ordinal)) continue; // listed below with proper names
                var name = s.IsLocal || string.IsNullOrWhiteSpace(s.Name) ? Loc.Get("settings.Meeting.Local") : s.Name;
                list.Add((s.Id, name, s.ColorHex, s.IsLocal ? Loc.Get("settings.Meeting.KindLocal") : "ICS"));
            }
            if (cal?.GoogleState == Core.Contracts.GoogleConnectionState.Connected)
                foreach (var g in cal.GoogleCalendars)
                    if (g.IsEnabled) list.Add(("google:" + g.Id, g.Name, g.ColorHex, "Google"));
        }
        catch (Exception ex) { Log.Warn("Settings: meeting calendars failed", ex); }

        var excluded = S.MeetingBar.ExcludedSources;
        var key = string.Join("\n", list.Select(x => $"{x.Id}|{x.Name}|{x.Color}"));
        if (key == _meetingCalendarsKey)
        {
            foreach (var item in MeetingCalendars) item.Sync(!excluded.Contains(item.Id, StringComparer.Ordinal));
            return;
        }
        _meetingCalendarsKey = key;
        MeetingCalendars.Clear();
        foreach (var (id, name, color, kind) in list)
            MeetingCalendars.Add(new MeetingCalendarItem(id, name, kind, ColorUtil.BrushOf(ColorUtil.Parse(color) ?? Color.FromRgb(0x8A, 0x8A, 0x8A)),
                !excluded.Contains(id, StringComparer.Ordinal), OnMeetingCalendarToggled));
    }

    private void OnMeetingCalendarToggled(MeetingCalendarItem item, bool included) => Save(s =>
    {
        s.MeetingBar.ExcludedSources.RemoveAll(x => x == item.Id);
        if (!included) s.MeetingBar.ExcludedSources.Add(item.Id);
    });

    partial void OnMeetingEnabledChanged(bool value) => Save(s => s.MeetingBar.Enabled = value);
    partial void OnMeetingShowOngoingChanged(bool value) => Save(s => s.MeetingBar.ShowOngoing = value);
    partial void OnMeetingWithMediaChanged(bool value) => Save(s => s.MeetingBar.ShowWithMedia = value);
    partial void OnMeetingSkipAllDayChanged(bool value) => Save(s => s.MeetingBar.SkipAllDay = value);

    partial void OnMeetingLeadChanged(double value)
    {
        OnPropertyChanged(nameof(MeetingLeadText));
        Defer("meetingLead", s => s.MeetingBar.LeadMinutes = (int)Math.Round(value));
    }

    partial void OnMeetingMaxTitleChanged(double value)
    {
        OnPropertyChanged(nameof(MeetingMaxTitleText));
        Defer("meetingTitle", s => s.MeetingBar.MaxTitleChars = (int)Math.Round(value));
    }

    partial void OnPeekGlanceWithMediaChanged(bool value) => Save(s => s.Notch.PeekGlanceWithMedia = value);
    partial void OnPhoneChargingHudChanged(bool value) => Save(s => s.Phone.ChargingHud = value);
    partial void OnPhoneCallStatusChanged(bool value) => Save(s => s.Phone.CallStatus = value);
    partial void OnPhoneOtpCopyChanged(bool value) => Save(s => s.Phone.OtpCopy = value);
}

/// <summary>One calendar in the meeting-bar checklist.</summary>
public sealed partial class MeetingCalendarItem : ObservableObject
{
    private readonly Action<MeetingCalendarItem, bool> _toggled;
    private bool _syncing;

    public MeetingCalendarItem(string id, string name, string kind, Brush brush, bool included, Action<MeetingCalendarItem, bool> toggled)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Brush = brush;
        _included = included;
        _toggled = toggled;
    }

    public string Id { get; }
    public string Name { get; }
    public string Kind { get; }
    public Brush Brush { get; }

    [ObservableProperty] private bool _included;

    internal void Sync(bool included)
    {
        _syncing = true;
        try { Included = included; }
        finally { _syncing = false; }
    }

    partial void OnIncludedChanged(bool value)
    {
        if (!_syncing) _toggled(this, value);
    }
}
