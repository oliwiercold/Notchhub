using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Settings;

/// <summary>Calendar section — Google Calendar card (client id/secret, connect/disconnect, calendars, default target).</summary>
public sealed partial class SettingsViewModel
{
    private const string CloudConsoleUrl = "https://console.cloud.google.com/apis/credentials";

    public ObservableCollection<GoogleCalendarItem> GoogleCalendars { get; } = [];
    [ObservableProperty] private ObservableCollection<Choice> _eventTargetChoices = [];
    [ObservableProperty] private string _googleClientId = "";
    [ObservableProperty] private string _googleClientSecret = "";
    [ObservableProperty] private string _googleStatusText = "";
    [ObservableProperty] private string? _googleErrorText;
    [ObservableProperty] private bool _isGoogleConnected;
    [ObservableProperty] private bool _isGoogleBusy;
    [ObservableProperty] private bool _canConnectGoogle;
    [ObservableProperty] private bool _canEditGoogleClient = true;
    [ObservableProperty] private bool _googleHelpOpen;
    private string? _googleCalendarsKey;
    private string? _eventTargetsKey;

    /// <summary>Re-reads the live Google state of the calendar service (UI thread).</summary>
    private void LoadGoogleStatus()
    {
        var c = AppServices.Calendar;
        var state = c?.GoogleState ?? GoogleConnectionState.NotConfigured;
        // The service sees a typed client id only after the deferred save — treat a typed id as "configured".
        if (state == GoogleConnectionState.NotConfigured && !string.IsNullOrWhiteSpace(GoogleClientId)) state = GoogleConnectionState.Disconnected;
        IsGoogleConnected = state == GoogleConnectionState.Connected;
        IsGoogleBusy = state == GoogleConnectionState.Connecting;
        CanConnectGoogle = c is not null && state is GoogleConnectionState.Disconnected or GoogleConnectionState.Error;
        CanEditGoogleClient = state is not (GoogleConnectionState.Connected or GoogleConnectionState.Connecting);
        GoogleStatusText = state switch
        {
            GoogleConnectionState.Connected => Loc.Format("settings.Calendar.Google.ConnectedAs", c?.GoogleAccountEmail ?? "Google"),
            GoogleConnectionState.Connecting => Loc.Get("settings.Calendar.Google.Connecting"),
            GoogleConnectionState.Disconnected => Loc.Get("settings.Calendar.Google.Disconnected"),
            GoogleConnectionState.Error => Loc.Get("settings.Calendar.Google.Failed"),
            _ => Loc.Get("settings.Calendar.Google.NotConfigured"),
        };
        GoogleErrorText = c?.GoogleError is { } err && state != GoogleConnectionState.Connecting ? GoogleErrorMessage(err) : null;

        var calendars = IsGoogleConnected ? c?.GoogleCalendars ?? [] : [];
        var key = string.Join("\n", calendars.Select(x => $"{x.Id}|{x.Name}|{x.ColorHex}|{x.CanWrite}|{x.IsPrimary}"));
        if (key != _googleCalendarsKey)
        {
            _googleCalendarsKey = key;
            GoogleCalendars.Clear();
            foreach (var cal in calendars) GoogleCalendars.Add(new GoogleCalendarItem(cal, OnGoogleCalendarToggled));
        }
        else
        {
            foreach (var item in GoogleCalendars)
                if (calendars.FirstOrDefault(x => x.Id == item.Id) is { } cal) item.SyncEnabled(cal.IsEnabled);
        }
        LoadEventTargets(calendars);
    }

    private void LoadEventTargets(IReadOnlyList<GoogleCalendarInfo> calendars)
    {
        var targets = new List<(string Label, object Value)> { (Loc.Get("settings.Calendar.Google.LocalTarget"), "local") };
        targets.AddRange(calendars.Where(x => x.CanWrite && x.IsEnabled).Select(x => (x.Name, (object)x.Id)));
        var current = S.Calendar.DefaultEventTarget ?? "local";
        if (!targets.Any(t => (string)t.Value == current)) current = "local";
        var key = current + "\n" + string.Join("\n", targets.Select(t => $"{t.Value}|{t.Label}"));
        if (key == _eventTargetsKey) return;
        _eventTargetsKey = key;
        EventTargetChoices = Choices.Build(targets, current, v => Save(s => s.Calendar.DefaultEventTarget = (string)v));
    }

    private static string GoogleErrorMessage(string code) => code switch
    {
        "access_denied" => Loc.Get("settings.Calendar.Google.Err.Denied"),
        "invalid_grant" => Loc.Get("settings.Calendar.Google.Err.Expired"),
        "invalid_client" or "unauthorized_client" or "invalid_request" => Loc.Get("settings.Calendar.Google.Err.Client"),
        "timeout" or "cancelled" => Loc.Get("settings.Calendar.Google.Err.Timeout"),
        "network" => Loc.Get("settings.Calendar.Google.Err.Network"),
        "state_mismatch" or "no_code" or "no_refresh_token" => Loc.Get("settings.Calendar.Google.Err.Flow"),
        _ => Loc.Format("settings.Calendar.Error", code),
    };

    partial void OnGoogleClientIdChanged(string value)
    {
        Defer("gid", s => s.Calendar.GoogleClientId = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        if (!_loading) LoadGoogleStatus();
    }

    partial void OnGoogleClientSecretChanged(string value) =>
        Defer("gsecret", s => s.Calendar.GoogleClientSecret = string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    private void OnGoogleCalendarToggled(GoogleCalendarItem item)
    {
        _selfUpdate = true;
        try { AppServices.Calendar?.SetGoogleCalendarEnabled(item.Id, item.Enabled); }
        catch (Exception ex) { Log.Warn("Settings: toggling a Google calendar failed", ex); }
        finally { _selfUpdate = false; }
        LoadGoogleStatus();
    }

    [RelayCommand]
    private async Task ConnectGoogle()
    {
        Flush();
        if (AppServices.Calendar is not { } c) return;
        IsGoogleBusy = true;
        CanConnectGoogle = false;
        try { await c.ConnectGoogleAsync(); }
        catch (Exception ex) { Log.Warn("Settings: Google sign-in failed", ex); }
        LoadGoogleStatus();
    }

    [RelayCommand]
    private async Task DisconnectGoogle()
    {
        if (AppServices.Calendar is not { } c) return;
        try { await c.DisconnectGoogleAsync(); }
        catch (Exception ex) { Log.Warn("Settings: Google disconnect failed", ex); }
        LoadGoogleStatus();
    }

    [RelayCommand] private void ToggleGoogleHelp() => GoogleHelpOpen = !GoogleHelpOpen;

    [RelayCommand]
    private static void OpenCloudConsole()
    {
        try { using (Process.Start(new ProcessStartInfo(CloudConsoleUrl) { UseShellExecute = true })) { } }
        catch (Exception ex) { Log.Warn("Settings: could not open Google Cloud Console", ex); }
    }
}

public sealed partial class GoogleCalendarItem(GoogleCalendarInfo info, Action<GoogleCalendarItem> toggled) : ObservableObject
{
    private bool _syncing;

    public string Id { get; } = info.Id;
    public string Name { get; } = info.Name;
    public Brush Brush { get; } = ColorUtil.BrushOf(ColorUtil.Parse(info.ColorHex) ?? Colors.CornflowerBlue);
    public string Detail { get; } = Loc.Get(info.IsPrimary ? "settings.Calendar.Google.Primary"
        : info.CanWrite ? "settings.Calendar.Google.Writable" : "settings.Calendar.Google.ReadOnly");
    [ObservableProperty] private bool _enabled = info.IsEnabled;

    internal void SyncEnabled(bool value)
    {
        _syncing = true;
        try { Enabled = value; }
        finally { _syncing = false; }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_syncing) toggled(this);
    }
}
