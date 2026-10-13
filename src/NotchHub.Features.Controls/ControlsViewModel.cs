using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Converters;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Controls;

public enum ControlsSheet { None, Network, Bluetooth, Audio }

public sealed partial class ControlsViewModel : ObservableObject
{
    private bool _shown;
    private bool _syncing;
    private IDisposable? _hold;
    private DispatcherTimer? _mediaTimer;
    private int _netPending, _btPending, _audioPending, _displayPending, _mediaPending;
    private CancellationTokenSource? _scanCts;
    private int? _pendingBrightness;
    private bool _brightnessPumping;

    // ───────────── network ─────────────
    [ObservableProperty] private string _netTitle = "Wi-Fi";
    [ObservableProperty] private string? _netSubtitle;
    [ObservableProperty] private string _netIcon = "wifi";
    [ObservableProperty] private bool _netActive;
    [ObservableProperty] private bool _netBusy;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _scanBlockedByLocation;
    [ObservableProperty] private bool _noNetworks;
    public ObservableCollection<WifiItemViewModel> Networks { get; } = [];

    // ───────────── bluetooth ─────────────
    [ObservableProperty] private string? _btSubtitle;
    [ObservableProperty] private string _btIcon = "bluetooth";
    [ObservableProperty] private bool _btActive;
    [ObservableProperty] private bool _btBusy;
    [ObservableProperty] private bool _noBtDevices;
    public ObservableCollection<BtDeviceViewModel> BtDevices { get; } = [];

    // ───────────── audio ─────────────
    [ObservableProperty] private string? _audioSubtitle;
    [ObservableProperty] private string _audioIcon = "speaker";
    [ObservableProperty] private bool _audioActive;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private string _volumeIcon = "volume_up";
    [ObservableProperty] private OutputViewModel? _selectedOutput;
    [ObservableProperty] private bool _noAppSessions;
    public bool IsDraggingVolume { get; set; }
    public ObservableCollection<OutputViewModel> Outputs { get; } = [];
    public ObservableCollection<AppSessionViewModel> AppSessions { get; } = [];

    // ───────────── display ─────────────
    [ObservableProperty] private bool _nightLightSupported;
    [ObservableProperty] private bool _nightLightOn;
    [ObservableProperty] private string? _nightLightSubtitle;
    [ObservableProperty] private bool _darkModeOn;
    [ObservableProperty] private string? _darkModeSubtitle;
    [ObservableProperty] private bool _brightnessSupported;
    [ObservableProperty] private double _brightness;
    public bool IsDraggingBrightness { get; set; }

    // ───────────── media ─────────────
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private string _mediaTitle = "";
    [ObservableProperty] private string _mediaArtist = "";
    [ObservableProperty] private string _mediaSource = "";
    [ObservableProperty] private byte[]? _mediaArt;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canPrevious;
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private bool _canPlayPause;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private bool _hasDuration;
    public bool IsSeeking { get; set; }
    public ObservableCollection<MediaChipViewModel> MediaChips { get; } = [];
    // shuffle / repeat (hidden when the session does not report them)
    [ObservableProperty] private bool _hasShuffle;
    [ObservableProperty] private bool _isShuffle;
    [ObservableProperty] private bool _hasRepeat;
    [ObservableProperty] private MediaRepeatMode _repeatMode;
    public bool IsRepeatOn => RepeatMode != MediaRepeatMode.None;
    public string RepeatIcon => RepeatMode == MediaRepeatMode.Track ? "repeat_one" : "repeat";
    partial void OnRepeatModeChanged(MediaRepeatMode value) { OnPropertyChanged(nameof(IsRepeatOn)); OnPropertyChanged(nameof(RepeatIcon)); }
    private MediaSessionInfo? _media;
    private string _chipSignature = "";
    private long _lastPosSecond = -1;

    // ───────────── sheet ─────────────
    [ObservableProperty] private ControlsSheet _sheet;
    [ObservableProperty] private bool _sheetOpen;
    [ObservableProperty] private string? _sheetTitle;

    // ═════════════ lifecycle ═════════════

    public void OnShown()
    {
        if (_shown) return;
        _shown = true;
        AppServices.Network.Changed += OnNetworkChanged;
        AppServices.Bluetooth.Changed += OnBluetoothChanged;
        AppServices.Audio.Changed += OnAudioChanged;
        AppServices.Audio.VolumeChanged += OnVolumeChangedExternal;
        AppServices.Display.Changed += OnDisplayChanged;
        AppServices.Media.Changed += OnMediaChanged;
        RefreshNetwork(); RefreshBluetooth(); RefreshAudio(); RefreshDisplay(); RefreshMedia();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        AppServices.Network.Changed -= OnNetworkChanged;
        AppServices.Bluetooth.Changed -= OnBluetoothChanged;
        AppServices.Audio.Changed -= OnAudioChanged;
        AppServices.Audio.VolumeChanged -= OnVolumeChangedExternal;
        AppServices.Display.Changed -= OnDisplayChanged;
        AppServices.Media.Changed -= OnMediaChanged;
        SheetOpen = false;
        _scanCts?.Cancel();
        UpdateMediaTimer();
    }

    /// <summary>Final release when the shell throws the view away.</summary>
    public void Release()
    {
        OnHidden();
        _hold?.Dispose();
        _hold = null;
        _mediaTimer?.Stop();
        _mediaTimer = null;
    }

    // Coalesce bursts of service events into one UI refresh each.
    private void Schedule(ref int flag, Action refresh)
    {
        if (Interlocked.Exchange(ref flag, 1) == 1) return;
        AppServices.Ui.Post(refresh);
    }

    private void OnNetworkChanged(object? s, EventArgs e) => Schedule(ref _netPending, () => { _netPending = 0; if (_shown) RefreshNetwork(); });
    private void OnBluetoothChanged(object? s, EventArgs e) => Schedule(ref _btPending, () => { _btPending = 0; if (_shown) RefreshBluetooth(); });
    private void OnAudioChanged(object? s, EventArgs e) => Schedule(ref _audioPending, () => { _audioPending = 0; if (_shown) RefreshAudio(); });
    private void OnVolumeChangedExternal(object? s, AudioVolumeChangedEventArgs e) => Schedule(ref _audioPending, () => { _audioPending = 0; if (_shown) RefreshAudio(); });
    private void OnDisplayChanged(object? s, EventArgs e) => Schedule(ref _displayPending, () => { _displayPending = 0; if (_shown) RefreshDisplay(); });
    private void OnMediaChanged(object? s, EventArgs e) => Schedule(ref _mediaPending, () => { _mediaPending = 0; if (_shown) RefreshMedia(); });

    // ═════════════ helpers ═════════════

    internal static string WifiIcon(int bars) => bars switch
    {
        >= 4 => "signal_wifi_4_bar",
        3 => "network_wifi_3_bar",
        2 => "network_wifi_2_bar",
        1 => "network_wifi_1_bar",
        _ => "signal_wifi_0_bar",
    };

    internal static string OutputIcon(AudioDeviceKind kind) => kind switch
    {
        AudioDeviceKind.Headphones => "headphones",
        AudioDeviceKind.Headset => "headset_mic",
        AudioDeviceKind.Hdmi => "tv",
        AudioDeviceKind.Bluetooth => "bluetooth_connected",
        AudioDeviceKind.Usb => "usb",
        _ => "speaker",
    };

    private static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"controls: cannot open {uri}: {ex.Message}"); }
    }

    private void OpenSheet(ControlsSheet sheet, string titleKey)
    {
        Sheet = sheet;
        SheetTitle = Loc.Get(titleKey);
        SheetOpen = true;
    }

    partial void OnSheetOpenChanged(bool value)
    {
        if (value) _hold ??= AppServices.Shell?.HoldOpen();
        else
        {
            _hold?.Dispose();
            _hold = null;
            _scanCts?.Cancel();
        }
    }

    [RelayCommand] private void CloseSheet() => SheetOpen = false;

    // ═════════════ network ═════════════

    private void RefreshNetwork()
    {
        var st = AppServices.Network.Status;
        switch (st.Kind)
        {
            case NetworkKind.WiFi:
                NetTitle = Loc.Get("controls.wifi");
                NetSubtitle = st.Name ?? Loc.Get("controls.connected");
                NetIcon = WifiIcon(st.SignalBars);
                NetActive = true;
                break;
            case NetworkKind.Ethernet:
                NetTitle = Loc.Get("controls.internet");
                NetSubtitle = Loc.Get("controls.ethernet");
                NetIcon = "lan";
                NetActive = true;
                break;
            case NetworkKind.Cellular:
            case NetworkKind.Other:
                NetTitle = Loc.Get("controls.internet");
                NetSubtitle = st.Name ?? Loc.Get("controls.connected");
                NetIcon = st.Kind == NetworkKind.Cellular ? "signal_cellular_alt" : "public";
                NetActive = true;
                break;
            default:
                bool wifi = st.WifiAdapterPresent;
                NetTitle = Loc.Get(wifi ? "controls.wifi" : "controls.internet");
                NetSubtitle = wifi && !st.WifiEnabled ? Loc.Get("common.off") : Loc.Get("controls.notConnected");
                NetIcon = wifi ? (st.WifiEnabled ? "signal_wifi_0_bar" : "wifi_off") : "public_off";
                NetActive = wifi && st.WifiEnabled;
                break;
        }
        if (st.Kind == NetworkKind.WiFi && !st.HasInternet) NetIcon = "signal_wifi_statusbar_not_connected";
    }

    [RelayCommand]
    private async Task ToggleNetwork()
    {
        var st = AppServices.Network.Status;
        if (!st.CanToggleWifi) { OpenNetworkSheet(); return; }
        if (NetBusy) return;
        NetBusy = true;
        try { await AppServices.Network.SetWifiEnabledAsync(!st.WifiEnabled); }
        catch (Exception ex) { Log.Warn($"controls: wifi toggle failed: {ex.Message}"); }
        finally { NetBusy = false; RefreshNetwork(); }
    }

    [RelayCommand]
    private void OpenNetworkSheet()
    {
        OpenSheet(ControlsSheet.Network, "controls.wifiNetworks");
        ScanCommand.Execute(null);
    }

    [RelayCommand]
    private async Task Scan()
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        IsScanning = true;
        NoNetworks = false;
        ScanBlockedByLocation = false;
        try
        {
            var list = await AppServices.Network.ScanWifiAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            string connected = Loc.Get("controls.connected"), saved = Loc.Get("controls.saved"),
                   secured = Loc.Get("controls.secured"), open = Loc.Get("controls.open");
            Networks.Clear();
            foreach (var n in list)
            {
                var detail = n.IsConnected ? connected : n.HasProfile ? saved : n.IsSecured ? secured : open;
                if (!string.IsNullOrEmpty(n.Band)) detail += " • " + n.Band;
                Networks.Add(new WifiItemViewModel { Network = n, Detail = detail });
            }
            ScanBlockedByLocation = AppServices.Network.LastScanBlockedByLocation;
            NoNetworks = Networks.Count == 0 && !ScanBlockedByLocation;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warn($"controls: wifi scan failed: {ex.Message}"); NoNetworks = Networks.Count == 0; }
        finally { if (ReferenceEquals(_scanCts, cts)) IsScanning = false; }
    }

    /// <summary>Row click: saved / open network → connect; secured unsaved → toggle the inline password field.</summary>
    [RelayCommand]
    private void ActivateNetwork(WifiItemViewModel? item)
    {
        if (item is null || item.IsBusy || item.IsConnected || item.JustConnected) return;
        if (item.Network.HasProfile || !item.IsSecured)
        {
            ConnectNetworkAsync(item, null).Forget("controls.connectWifi");
            return;
        }
        var open = !item.IsPasswordOpen;
        foreach (var other in Networks) other.IsPasswordOpen = false;
        item.Failed = false;
        item.IsPasswordOpen = open;
    }

    /// <summary>Connect with the typed password (from the view's PasswordBox; Enter or "Połącz").</summary>
    internal void ConnectWithPassword(WifiItemViewModel item, string password)
    {
        if (item.IsBusy || string.IsNullOrEmpty(password)) return;
        ConnectNetworkAsync(item, password).Forget("controls.connectWifi");
    }

    private async Task ConnectNetworkAsync(WifiItemViewModel item, string? password)
    {
        var n = item.Network;
        item.IsBusy = true;
        item.Failed = false;
        NetBusy = true;
        bool ok;
        try
        {
            ok = password is null && n.HasProfile
                ? await AppServices.Network.ConnectWifiAsync(n.Ssid)
                : await AppServices.Network.ConnectWifiAsync(n.Ssid, password);
        }
        catch (Exception ex)
        {
            Log.Warn($"controls: wifi connect failed: {ex.Message}");
            ok = false;
        }
        finally
        {
            item.IsBusy = false;
            NetBusy = false;
            RefreshNetwork();
        }

        if (ok)
        {
            item.IsPasswordOpen = false;
            item.JustConnected = true;
            item.Detail = Loc.Get("controls.connected");
            await Task.Delay(1500);
            if (_shown && SheetOpen && Sheet == ControlsSheet.Network && !Networks.Any(x => x.IsPasswordOpen || x.IsBusy)) await Scan();
        }
        else
        {
            item.Failed = true;
            // A saved profile that no longer works (password changed) → offer the password field.
            if (n.IsSecured) item.IsPasswordOpen = true;
        }
    }

    [RelayCommand] private static void OpenNetworkSettings() => AppServices.Network.OpenSettings();
    [RelayCommand] private static void OpenLocationSettings() => OpenUri("ms-settings:privacy-location");

    // ═════════════ bluetooth ═════════════

    private void RefreshBluetooth()
    {
        var bt = AppServices.Bluetooth;
        var connected = bt.Devices.FirstOrDefault(d => d.IsConnected);
        BtActive = bt.IsAvailable && bt.IsEnabled;
        BtIcon = !bt.IsAvailable || !bt.IsEnabled ? "bluetooth_disabled" : connected is not null ? "bluetooth_connected" : "bluetooth";
        BtSubtitle = !bt.IsAvailable ? Loc.Get("common.unavailable")
                   : !bt.IsEnabled ? Loc.Get("common.off")
                   : connected?.Name ?? Loc.Get("common.on");

        string con = Loc.Get("controls.connected"), paired = Loc.Get("controls.paired"),
               connect = Loc.Get("controls.connect"), disconnect = Loc.Get("controls.disconnect");
        BtDevices.Clear();
        foreach (var d in bt.Devices)
            BtDevices.Add(new BtDeviceViewModel
            {
                Device = d,
                StatusText = d.IsConnected ? con : paired,
                ActionText = d.IsConnected ? disconnect : connect,
            });
        NoBtDevices = BtDevices.Count == 0;
    }

    [RelayCommand]
    private async Task ToggleBluetooth()
    {
        var bt = AppServices.Bluetooth;
        if (!bt.IsAvailable || !bt.CanToggle) { bt.OpenSettings(); return; }
        if (BtBusy) return;
        BtBusy = true;
        try { await bt.SetEnabledAsync(!bt.IsEnabled); }
        catch (Exception ex) { Log.Warn($"controls: bluetooth toggle failed: {ex.Message}"); }
        finally { BtBusy = false; RefreshBluetooth(); }
    }

    [RelayCommand] private void OpenBluetoothSheet() => OpenSheet(ControlsSheet.Bluetooth, "controls.btDevices");

    [RelayCommand]
    private async Task ToggleDevice(BtDeviceViewModel? d)
    {
        if (d is null || d.IsBusy) return;
        d.IsBusy = true;
        try
        {
            var bt = AppServices.Bluetooth;
            bool ok = d.IsConnected ? await bt.DisconnectAsync(d.Id) : await bt.ConnectAsync(d.Id);
            if (!ok) bt.OpenSettings();
        }
        catch (Exception ex) { Log.Warn($"controls: bluetooth device action failed: {ex.Message}"); }
        finally { d.IsBusy = false; RefreshBluetooth(); }
    }

    [RelayCommand] private static void OpenBluetoothSettings() => AppServices.Bluetooth.OpenSettings();

    // ═════════════ audio ═════════════

    private void RefreshAudio()
    {
        var a = AppServices.Audio;
        _syncing = true;
        try
        {
            var dev = a.DefaultOutput;
            AudioIcon = !a.IsAvailable ? "volume_off" : a.IsMuted ? "volume_off" : OutputIcon(dev?.Kind ?? AudioDeviceKind.Speakers);
            AudioActive = a.IsAvailable && !a.IsMuted;
            AudioSubtitle = !a.IsAvailable ? Loc.Get("common.unavailable")
                          : a.IsMuted ? Loc.Get("controls.muted")
                          : dev?.Name ?? Loc.Get("common.unavailable");
            if (!IsDraggingVolume) Volume = Math.Round(a.Volume * 100);
            UpdateVolumeIcon();

            // outputs (rebuild only when the set / default changed)
            var outs = a.Outputs;
            if (outs.Count != Outputs.Count || outs.Where((o, i) => Outputs[i].Device != o).Any())
            {
                Outputs.Clear();
                foreach (var o in outs) Outputs.Add(new OutputViewModel { Device = o });
            }
            SelectedOutput = Outputs.FirstOrDefault(o => o.IsDefault);

            // mixer (update in place so sliders being dragged are not recreated)
            var sessions = a.Sessions;
            for (int i = AppSessions.Count - 1; i >= 0; i--)
                if (!sessions.Any(s => s.Id == AppSessions[i].Id)) AppSessions.RemoveAt(i);
            foreach (var s in sessions)
            {
                var vm = AppSessions.FirstOrDefault(x => x.Id == s.Id);
                if (vm is null) AppSessions.Add(new AppSessionViewModel(s));
                else vm.Update(s);
            }
            NoAppSessions = AppSessions.Count == 0;
        }
        finally { _syncing = false; }
    }

    private void UpdateVolumeIcon()
    {
        var a = AppServices.Audio;
        VolumeIcon = a.IsMuted ? "volume_off" : Volume <= 0 ? "volume_mute" : Volume < 50 ? "volume_down" : "volume_up";
    }

    partial void OnVolumeChanged(double value)
    {
        if (_syncing) return;
        var a = AppServices.Audio;
        a.SetVolume((float)(value / 100.0));
        if (a.IsMuted && value > 0) a.SetMuted(false);
        UpdateVolumeIcon();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        var a = AppServices.Audio;
        if (!a.IsAvailable) { a.OpenSettings(); return; }
        a.SetMuted(!a.IsMuted);
        RefreshAudio();
    }

    [RelayCommand] private void OpenAudioSheet() => OpenSheet(ControlsSheet.Audio, "controls.audioSheet");

    partial void OnSelectedOutputChanged(OutputViewModel? value)
    {
        if (_syncing || value is null || value.IsDefault) return;
        SetOutputAsync(value.Id).Forget("controls.setOutput");
    }

    private async Task SetOutputAsync(string id)
    {
        try { await AppServices.Audio.SetDefaultOutputAsync(id); }
        finally { RefreshAudio(); }
    }

    [RelayCommand] private static void OpenSoundSettings() => AppServices.Audio.OpenSettings();

    // ═════════════ display ═════════════

    private void RefreshDisplay()
    {
        var d = AppServices.Display;
        _syncing = true;
        try
        {
            NightLightSupported = d.NightLightSupported;
            NightLightOn = d.NightLightSupported && d.NightLightEnabled;
            NightLightSubtitle = !d.NightLightSupported ? Loc.Get("common.unavailable") : Loc.Get(NightLightOn ? "common.on" : "common.off");
            DarkModeOn = d.DarkModeEnabled;
            DarkModeSubtitle = Loc.Get(DarkModeOn ? "common.on" : "common.off");
            BrightnessSupported = d.BrightnessSupported;
            if (!IsDraggingBrightness && _pendingBrightness is null) Brightness = d.Brightness;
        }
        finally { _syncing = false; }
    }

    [RelayCommand]
    private async Task ToggleNightLight()
    {
        var d = AppServices.Display;
        if (!d.NightLightSupported) { d.OpenNightLightSettings(); return; }
        try { if (!await d.SetNightLightAsync(!d.NightLightEnabled)) d.OpenNightLightSettings(); }
        finally { RefreshDisplay(); }
    }

    [RelayCommand] private static void OpenNightLightSettings() => AppServices.Display.OpenNightLightSettings();

    [RelayCommand]
    private async Task ToggleDarkMode()
    {
        var d = AppServices.Display;
        try { await d.SetDarkModeAsync(!d.DarkModeEnabled); }
        finally { RefreshDisplay(); }
    }

    partial void OnBrightnessChanged(double value)
    {
        if (_syncing) return;
        _pendingBrightness = (int)Math.Round(value);
        if (!_brightnessPumping) PumpBrightnessAsync().Forget("controls.brightness");
    }

    // DDC/CI is slow: keep at most one write in flight and always end on the latest value.
    private async Task PumpBrightnessAsync()
    {
        _brightnessPumping = true;
        try
        {
            while (_pendingBrightness is int v)
            {
                _pendingBrightness = null;
                await AppServices.Display.SetBrightnessAsync(v);
            }
        }
        finally { _brightnessPumping = false; }
    }

    // ═════════════ media ═════════════

    private void RefreshMedia()
    {
        var m = AppServices.Media;
        var cur = m.Current;
        _media = cur;
        HasMedia = cur is not null;
        if (cur is not null)
        {
            MediaTitle = string.IsNullOrWhiteSpace(cur.Title) ? Loc.Get("controls.unknownTitle") : cur.Title;
            MediaArtist = cur.Artist;
            var dev = AppServices.Audio.DefaultOutput?.Name;
            MediaSource = string.IsNullOrEmpty(dev) ? cur.SourceAppName
                        : string.IsNullOrEmpty(cur.SourceAppName) ? dev : $"{dev} • {cur.SourceAppName}";
            if (!ReferenceEquals(MediaArt, cur.Thumbnail)) MediaArt = cur.Thumbnail;
            IsPlaying = cur.IsPlaying;
            CanPrevious = cur.CanPrevious;
            CanNext = cur.CanNext;
            CanPlayPause = cur.CanPlayPause;
            CanSeek = cur.CanSeek && cur.Duration > TimeSpan.Zero;
            HasDuration = cur.Duration > TimeSpan.Zero;
            HasShuffle = cur.IsShuffleActive is not null;
            IsShuffle = cur.IsShuffleActive == true;
            HasRepeat = cur.RepeatMode is not null;
            RepeatMode = cur.RepeatMode ?? MediaRepeatMode.None;
            DurationText = TimeSpanClock.ToClock(cur.Duration);
            _lastPosSecond = -1;
            UpdatePosition();
        }
        else
        {
            IsPlaying = false;
            MediaArt = null;
            HasShuffle = HasRepeat = false;
        }
        RefreshSpotify(cur);

        var sessions = m.Sessions;
        var sig = sessions.Count > 1 ? string.Join('|', sessions.Select(s => s.SessionId + s.SourceAppName)) + "#" + cur?.SessionId : "";
        if (sig != _chipSignature)
        {
            _chipSignature = sig;
            MediaChips.Clear();
            if (sessions.Count > 1)
                foreach (var s in sessions)
                    MediaChips.Add(new MediaChipViewModel(this, s.SessionId,
                        string.IsNullOrEmpty(s.SourceAppName) ? s.SourceAppId : s.SourceAppName, s.SessionId == cur?.SessionId));
        }
        UpdateMediaTimer();
    }

    internal void SelectMediaSession(string id) => AppServices.Media.SelectSession(id);

    private void UpdatePosition()
    {
        var cur = _media;
        if (cur is null || IsSeeking) return;
        var pos = cur.GetLivePosition(DateTimeOffset.Now);
        Progress = cur.Duration > TimeSpan.Zero ? Math.Clamp(pos.TotalSeconds / cur.Duration.TotalSeconds, 0, 1) : 0;
        long sec = (long)pos.TotalSeconds;
        if (sec != _lastPosSecond)
        {
            _lastPosSecond = sec;
            PositionText = TimeSpanClock.ToClock(TimeSpan.FromSeconds(sec));
        }
    }

    private void UpdateMediaTimer()
    {
        bool run = _shown && _media is { IsPlaying: true } && _media.Duration > TimeSpan.Zero;
        if (run)
        {
            if (_mediaTimer is null)
            {
                // 4 Hz is enough: the time label changes once a second and the bar moves ~1 px every few seconds
                _mediaTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
                _mediaTimer.Tick += (_, _) => UpdatePosition();
            }
            _mediaTimer.Start();
        }
        else _mediaTimer?.Stop();
    }

    public void Seek(double fraction)
    {
        var cur = _media;
        if (cur is null || cur.Duration <= TimeSpan.Zero) return;
        Progress = fraction;
        AppServices.Media.SeekAsync(TimeSpan.FromTicks((long)(cur.Duration.Ticks * Math.Clamp(fraction, 0, 1)))).Forget("controls.seek");
    }

    [RelayCommand] private static Task PlayPause() => AppServices.Media.PlayPauseAsync();
    [RelayCommand] private static Task Next() => AppServices.Media.NextAsync();
    [RelayCommand] private static Task Previous() => AppServices.Media.PreviousAsync();

    [RelayCommand]
    private async Task ToggleShuffle()
    {
        IsShuffle = !IsShuffle; // optimistic; the session echo confirms
        try { await AppServices.Media.SetShuffleAsync(IsShuffle); }
        catch (Exception ex) { Log.Warn($"controls: shuffle failed: {ex.Message}"); }
    }

    /// <summary>Off → all → one → off.</summary>
    [RelayCommand]
    private async Task CycleRepeat()
    {
        RepeatMode = RepeatMode switch { MediaRepeatMode.None => MediaRepeatMode.List, MediaRepeatMode.List => MediaRepeatMode.Track, _ => MediaRepeatMode.None };
        try { await AppServices.Media.SetRepeatAsync(RepeatMode); }
        catch (Exception ex) { Log.Warn($"controls: repeat failed: {ex.Message}"); }
    }
}
