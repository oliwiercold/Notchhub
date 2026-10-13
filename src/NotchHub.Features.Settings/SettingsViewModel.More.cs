using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Settings;

public sealed partial class SettingsViewModel
{
    // ═════════════ Skróty ═════════════

    public ObservableCollection<HotkeyField> Hotkeys { get; } = [];
    private HotkeyField? _capturing;
    private IDisposable? _hold;

    private void LoadShortcuts()
    {
        if (_capturing is not null) return;
        Hotkeys.Clear();
        Hotkeys.Add(new HotkeyField("TogglePanel", "settings.Shortcuts.TogglePanel", S.Shortcuts.TogglePanel, StartCapture));
        Hotkeys.Add(new HotkeyField("OpenClipboard", "settings.Shortcuts.OpenClipboard", S.Shortcuts.OpenClipboard, StartCapture));
    }

    private void StartCapture(HotkeyField f)
    {
        if (_capturing is not null && _capturing != f) _capturing.IsCapturing = false;
        _capturing = f;
        f.Error = null;
        f.IsCapturing = true;
        _hold ??= AppServices.Shell?.HoldOpen();
    }

    public void CancelCapture()
    {
        if (_capturing is not null) _capturing.IsCapturing = false;
        _capturing = null;
        _hold?.Dispose();
        _hold = null;
    }

    /// <summary>Called by the view for every key while a field is capturing. Returns true when handled.</summary>
    public bool HandleKey(Key key, ModifierKeys mods)
    {
        var f = _capturing;
        if (f is null) return false;
        switch (key)
        {
            case Key.Escape when mods == ModifierKeys.None:
                CancelCapture();
                return true;
            case Key.Back when mods == ModifierKeys.None:
                CommitGesture(f, "");
                return true;
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
                or Key.System or Key.None:
                return true;
        }
        if (mods == ModifierKeys.None || mods == ModifierKeys.Shift)
        {
            f.Error = Loc.Get("settings.Shortcuts.NeedModifier");
            return true;
        }
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(key));
        var gesture = string.Join("+", parts);
        bool valid;
        try { valid = AppServices.Hotkeys?.IsValidGesture(gesture) ?? true; }
        catch { valid = false; }
        if (!valid)
        {
            f.Error = Loc.Format("settings.Shortcuts.Invalid", gesture);
            return true;
        }
        CommitGesture(f, gesture);
        return true;
    }

    private void CommitGesture(HotkeyField f, string gesture)
    {
        f.Gesture = gesture;
        f.Error = null;
        CancelCapture();
        Save(s =>
        {
            if (f.Id == "TogglePanel") s.Shortcuts.TogglePanel = gesture;
            else s.Shortcuts.OpenClipboard = gesture;
        });
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + (int)(key - Key.NumPad0),
        Key.Return => "Enter",
        Key.Oem3 => "OemTilde",
        _ => key.ToString(),
    };

    // ═════════════ Schowek ═════════════

    [ObservableProperty] private bool _clipEnabled;
    [ObservableProperty] private double _clipMaxItems;
    [ObservableProperty] private bool _clipSaveImages;
    [ObservableProperty] private bool _clipIgnoreSensitive;
    [ObservableProperty] private double _clipRetentionDays;
    [ObservableProperty] private string _newIgnoredApp = "";
    [ObservableProperty] private bool _clearConfirm;
    [ObservableProperty] private bool _clearDone;
    public ObservableCollection<TextItem> IgnoredApps { get; } = [];

    public string ClipMaxItemsText => $"{ClipMaxItems:0}";
    public string ClipRetentionText => Loc.Format("settings.Clipboard.DaysFormat", (int)Math.Round(ClipRetentionDays));

    private void LoadClipboard()
    {
        var c = S.Clipboard;
        ClipEnabled = c.Enabled;
        ClipMaxItems = c.MaxItems;
        ClipSaveImages = c.SaveImages;
        ClipIgnoreSensitive = c.IgnoreSensitive;
        ClipRetentionDays = c.RetentionDays;
        OnPropertyChanged(nameof(ClipRetentionText));
        IgnoredApps.Clear();
        foreach (var app in c.IgnoredApps) IgnoredApps.Add(new TextItem(app, RemoveIgnoredApp));
    }

    partial void OnClipEnabledChanged(bool value) => Save(s => s.Clipboard.Enabled = value);
    partial void OnClipSaveImagesChanged(bool value) => Save(s => s.Clipboard.SaveImages = value);
    partial void OnClipIgnoreSensitiveChanged(bool value) => Save(s => s.Clipboard.IgnoreSensitive = value);

    partial void OnClipMaxItemsChanged(double value)
    {
        OnPropertyChanged(nameof(ClipMaxItemsText));
        Defer("cmax", s => s.Clipboard.MaxItems = (int)Math.Round(value));
    }

    partial void OnClipRetentionDaysChanged(double value)
    {
        OnPropertyChanged(nameof(ClipRetentionText));
        Defer("cret", s => s.Clipboard.RetentionDays = (int)Math.Round(value));
    }

    [RelayCommand]
    private void AddIgnoredApp()
    {
        var name = NewIgnoredApp.Trim();
        if (name.Length == 0) return;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        NewIgnoredApp = "";
        if (IgnoredApps.Any(a => string.Equals(a.Text, name, StringComparison.OrdinalIgnoreCase))) return;
        IgnoredApps.Add(new TextItem(name, RemoveIgnoredApp));
        Save(s => s.Clipboard.IgnoredApps = IgnoredApps.Select(a => a.Text).ToList());
    }

    private void RemoveIgnoredApp(TextItem item)
    {
        IgnoredApps.Remove(item);
        Save(s => s.Clipboard.IgnoredApps = IgnoredApps.Select(a => a.Text).ToList());
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (!ClearConfirm)
        {
            ClearConfirm = true;
            ClearDone = false;
            return;
        }
        ClearConfirm = false;
        try
        {
            AppServices.Clipboard?.Clear(keepPinned: true);
            ClearDone = true;
        }
        catch (Exception ex) { Log.Warn("Settings: clipboard clear failed", ex); }
    }

    [RelayCommand] private void CancelClear() => ClearConfirm = false;

    // ═════════════ Pogoda ═════════════

    [ObservableProperty] private bool _weatherEnabled;
    [ObservableProperty] private string _citySearch = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _searchEmpty;
    [ObservableProperty] private string _locationText = "";
    [ObservableProperty] private ObservableCollection<Choice> _unitChoices = [];
    public ObservableCollection<LocationResult> LocationResults { get; } = [];
    private CancellationTokenSource? _searchCts;

    private void LoadWeather()
    {
        var w = S.Weather;
        WeatherEnabled = w.Enabled;
        LocationText = string.IsNullOrWhiteSpace(w.LocationName ?? w.City)
            ? Loc.Get("settings.Weather.FromIp")
            : (w.LocationName ?? w.City)!;
        UnitChoices = Choices.Build([("°C", TemperatureUnit.Celsius), ("°F", TemperatureUnit.Fahrenheit)], w.Unit,
            v => { Save(s => s.Weather.Unit = (TemperatureUnit)v); RefreshWeather(); });
    }

    partial void OnWeatherEnabledChanged(bool value) => Save(s => s.Weather.Enabled = value);

    [RelayCommand]
    private async Task SearchCity()
    {
        var q = CitySearch.Trim();
        if (q.Length < 2) return;
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        IsSearching = true;
        SearchEmpty = false;
        try
        {
            var results = await AppServices.Weather.SearchLocationAsync(q, cts.Token);
            if (cts != _searchCts) return;
            LocationResults.Clear();
            foreach (var r in results.Take(6)) LocationResults.Add(new LocationResult(r, PickLocation));
            SearchEmpty = LocationResults.Count == 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn("Settings: location search failed", ex);
            SearchEmpty = true;
        }
        finally
        {
            if (cts == _searchCts) IsSearching = false;
        }
    }

    private void PickLocation(LocationResult r)
    {
        var l = r.Location;
        var name = string.IsNullOrWhiteSpace(r.Detail) ? l.Name : $"{l.Name}, {l.Country ?? l.Region}";
        LocationResults.Clear();
        CitySearch = "";
        LocationText = name;
        Save(s =>
        {
            s.Weather.City = l.Name;
            s.Weather.Latitude = l.Latitude;
            s.Weather.Longitude = l.Longitude;
            s.Weather.LocationName = name;
        });
        RefreshWeather();
    }

    [RelayCommand]
    private void UseIpLocation()
    {
        LocationResults.Clear();
        LocationText = Loc.Get("settings.Weather.FromIp");
        Save(s =>
        {
            s.Weather.City = null;
            s.Weather.Latitude = null;
            s.Weather.Longitude = null;
            s.Weather.LocationName = null;
        });
        RefreshWeather();
    }

    private static void RefreshWeather()
    {
        try { AppServices.Weather?.RefreshAsync().Forget("Settings weather refresh"); }
        catch (Exception ex) { Log.Warn("Settings: weather refresh failed", ex); }
    }

    // ═════════════ Kalendarz ═════════════

    public ObservableCollection<SubscriptionItem> Subscriptions { get; } = [];
    public ObservableCollection<ColorDot> CalendarColors { get; } = [];
    [ObservableProperty] private string _newCalName = "";
    [ObservableProperty] private string _newCalUrl = "";
    [ObservableProperty] private bool _newCalInvalid;
    [ObservableProperty] private bool _weekStartsMonday;
    [ObservableProperty] private double _calRefreshMinutes;
    private string _newCalColor = "#F28FAD";

    public string CalRefreshText => Loc.Format("settings.Calendar.MinutesFormat", (int)Math.Round(CalRefreshMinutes));

    private void LoadCalendar()
    {
        var c = S.Calendar;
        WeekStartsMonday = c.WeekStartsMonday;
        CalRefreshMinutes = c.RefreshMinutes;
        OnPropertyChanged(nameof(CalRefreshText));
        Subscriptions.Clear();
        foreach (var sub in c.Subscriptions) Subscriptions.Add(new SubscriptionItem(sub, SubscriptionChanged, RemoveSubscription));
        if (CalendarColors.Count == 0)
            foreach (var hex in new[] { "#F28FAD", "#8AB4F8", "#81C995", "#FDD663", "#C58AF9", "#78D9EC", "#FCAD70" })
                CalendarColors.Add(new ColorDot(hex, d =>
                {
                    _newCalColor = d.Hex;
                    foreach (var x in CalendarColors) x.IsSelected = x == d;
                }) { IsSelected = hex == _newCalColor });
        GoogleClientId = c.GoogleClientId ?? "";
        GoogleClientSecret = c.GoogleClientSecret ?? "";
        _eventTargetsKey = null;
        LoadCalendarStatus();
    }

    private void LoadCalendarStatus()
    {
        var sources = AppServices.Calendar?.Sources ?? [];
        foreach (var item in Subscriptions)
        {
            var src = sources.FirstOrDefault(s => s.Id == item.Id);
            item.HasError = src?.Error is not null;
            item.Status = !item.Enabled ? Loc.Get("settings.Calendar.Disabled")
                : src?.Error is { } err ? Loc.Format("settings.Calendar.Error", err)
                : src?.LastSync is { } t ? Loc.Format("settings.Calendar.Synced", t.ToLocalTime().ToString("g", Loc.Culture))
                : Loc.Get("settings.Calendar.Pending");
        }
        LoadGoogleStatus();
    }

    partial void OnWeekStartsMondayChanged(bool value) => Save(s => s.Calendar.WeekStartsMonday = value);

    partial void OnCalRefreshMinutesChanged(double value)
    {
        OnPropertyChanged(nameof(CalRefreshText));
        Defer("crefresh", s => s.Calendar.RefreshMinutes = (int)Math.Round(value));
    }

    private void SubscriptionChanged(SubscriptionItem item)
    {
        Save(s =>
        {
            var sub = s.Calendar.Subscriptions.FirstOrDefault(x => x.Id == item.Id);
            if (sub is not null) sub.Enabled = item.Enabled;
        });
        LoadCalendarStatus();
        RefreshCalendar();
    }

    private void RemoveSubscription(SubscriptionItem item)
    {
        Subscriptions.Remove(item);
        Save(s => s.Calendar.Subscriptions.RemoveAll(x => x.Id == item.Id));
        RefreshCalendar();
    }

    [RelayCommand]
    private void AddSubscription()
    {
        var url = NewCalUrl.Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            NewCalInvalid = true;
            return;
        }
        NewCalInvalid = false;
        var sub = new CalendarSubscription
        {
            Name = string.IsNullOrWhiteSpace(NewCalName) ? uri.Host : NewCalName.Trim(),
            Url = url,
            ColorHex = _newCalColor,
        };
        NewCalName = "";
        NewCalUrl = "";
        Subscriptions.Add(new SubscriptionItem(sub, SubscriptionChanged, RemoveSubscription));
        Save(s => s.Calendar.Subscriptions.Add(sub));
        LoadCalendarStatus();
        RefreshCalendar();
    }

    private static void RefreshCalendar()
    {
        try { AppServices.Calendar?.RefreshAsync().Forget("Settings calendar refresh"); }
        catch (Exception ex) { Log.Warn("Settings: calendar refresh failed", ex); }
    }

    // ═════════════ Telefon ═════════════

    [ObservableProperty] private bool _phoneEnabled;
    [ObservableProperty] private string _phoneDeviceName = "";
    [ObservableProperty] private bool _notificationPeeks;
    [ObservableProperty] private bool _receiveClipboard;
    [ObservableProperty] private bool _phoneInHeader;
    [ObservableProperty] private string _phoneStatus = "";
    [ObservableProperty] private bool _phoneRunning;
    [ObservableProperty] private string _localDeviceId = "";
    [ObservableProperty] private string _newMutedApp = "";
    public ObservableCollection<DeviceItem> PairedDevices { get; } = [];
    public ObservableCollection<TextItem> MutedApps { get; } = [];

    private void LoadPhone()
    {
        var p = S.Phone;
        PhoneEnabled = p.Enabled;
        PhoneDeviceName = p.DeviceName ?? AppServices.Phone?.LocalDeviceName ?? Environment.MachineName;
        NotificationPeeks = p.NotificationPeeks;
        ReceiveClipboard = p.ReceiveClipboard;
        PhoneInHeader = p.ShowStatusInHeader;
        MutedApps.Clear();
        foreach (var app in p.MutedApps) MutedApps.Add(new TextItem(app, RemoveMutedApp));
        LoadPhoneState();
    }

    private void LoadPhoneState()
    {
        var phone = AppServices.Phone;
        PhoneRunning = phone?.IsRunning == true;
        PhoneStatus = Loc.Get(PhoneRunning ? "settings.Phone.Running" : S.Phone.Enabled ? "settings.Phone.Starting" : "settings.Phone.Stopped");
        LocalDeviceId = phone?.LocalDeviceId ?? "—";
        PairedDevices.Clear();
        foreach (var d in phone?.Devices ?? [])
        {
            if (d.PairState != PhonePairState.Paired) continue;
            PairedDevices.Add(new DeviceItem(d, Loc.Get(d.IsReachable ? "settings.Phone.Connected" : "settings.Phone.Offline"), Unpair));
        }
    }

    partial void OnPhoneEnabledChanged(bool value)
    {
        Save(s => s.Phone.Enabled = value);
        if (!_loading) LoadPhoneState();
    }

    partial void OnNotificationPeeksChanged(bool value) => Save(s => s.Phone.NotificationPeeks = value);
    partial void OnReceiveClipboardChanged(bool value) => Save(s => s.Phone.ReceiveClipboard = value);
    partial void OnPhoneInHeaderChanged(bool value) => Save(s => s.Phone.ShowStatusInHeader = value);

    [RelayCommand]
    private void CommitDeviceName()
    {
        var name = PhoneDeviceName.Trim();
        var value = name.Length == 0 || name == Environment.MachineName ? null : name;
        if (value == S.Phone.DeviceName) return;
        Save(s => s.Phone.DeviceName = value);
    }

    private void Unpair(DeviceItem item) => UnpairAsync(item).Forget("Settings unpair");

    private async Task UnpairAsync(DeviceItem item)
    {
        try
        {
            item.IsBusy = true;
            await AppServices.Phone.UnpairAsync(item.Id);
        }
        catch (Exception ex) { Log.Warn("Settings: unpair failed", ex); }
        finally
        {
            item.IsBusy = false;
            LoadPhoneState();
        }
    }

    [RelayCommand]
    private void AddMutedApp()
    {
        var name = NewMutedApp.Trim();
        NewMutedApp = "";
        if (name.Length == 0 || MutedApps.Any(a => string.Equals(a.Text, name, StringComparison.OrdinalIgnoreCase))) return;
        MutedApps.Add(new TextItem(name, RemoveMutedApp));
        Save(s => s.Phone.MutedApps = MutedApps.Select(a => a.Text).ToList());
    }

    private void RemoveMutedApp(TextItem item)
    {
        MutedApps.Remove(item);
        Save(s => s.Phone.MutedApps = MutedApps.Select(a => a.Text).ToList());
    }

    // ═════════════ Ogólne ═════════════

    [ObservableProperty] private ObservableCollection<Choice> _languageChoices = [];
    [ObservableProperty] private bool _startWithWindows;

    public string VersionText { get; } = "NotchHub " + AppVersion;

    /// <summary>Entry assembly informational version (without the "+commit" suffix), falling back to the file version.</summary>
    public static string AppVersion { get; } = ReadVersion();
    public string VersionLabel => "v" + AppVersion;

    private const string RepoUrl = "https://github.com/oliwiercold/NotchHub";
    private const string WebsiteUrl = "https://oliwiercold.github.io/Notchhub";
    private const string BuyMeACoffeeUrl = "https://www.buymeacoffee.com/gimpostin";

    private static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    [RelayCommand] private static void OpenRepo() => OpenUrl(RepoUrl);
    [RelayCommand] private static void OpenWebsite() => OpenUrl(WebsiteUrl);
    [RelayCommand] private static void OpenBuyMeACoffee() => OpenUrl(BuyMeACoffeeUrl);

    private static void OpenUrl(string url)
    {
        try { using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { } }
        catch (Exception ex) { Log.Warn("Settings: open url failed", ex); }
    }

    public string DataFolder => AppPaths.Root;

    private void LoadGeneral()
    {
        LanguageChoices = Choices.Build(
        [
            ("Polski", "pl"), ("English", "en"), (Loc.Get("settings.General.LanguageAuto"), "auto"),
        ], S.Language, v => Save(s => s.Language = (string)v), v => "");
        // The Run key (and Windows' "Startup apps" toggle) is the source of truth; settings.json only mirrors it,
        // so a change made outside NotchHub (Task Manager, installer upgrade) is shown and not reverted on next launch.
        bool live = S.General.StartWithWindows;
        try { live = AppServices.Autostart?.IsEnabled ?? live; }
        catch (Exception ex) { Log.Warn("Settings: autostart state read failed", ex); }
        StartWithWindows = live;
        if (live != S.General.StartWithWindows)
            AppServices.Ui.Post(() => { if (S.General.StartWithWindows != live) Save(s => s.General.StartWithWindows = live); });
    }

    partial void OnStartWithWindowsChanged(bool value) => Save(s => s.General.StartWithWindows = value);

    [RelayCommand] private static void OpenDataFolder() => OpenFolder(AppPaths.Root);
    [RelayCommand] private static void OpenLogs() => OpenFolder(AppPaths.Logs);

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Settings: open folder failed", ex); }
    }
}
