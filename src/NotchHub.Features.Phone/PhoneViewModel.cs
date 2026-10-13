using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Phone;

public enum PhoneMode { Disabled, Setup, PairOutgoing, PairIncoming, Connected, Offline }

public enum BatteryTone { Normal, Warning, Low }

/// <summary>Immutable view of the phone service (also used by PreviewStates to fake states).</summary>
internal sealed record PhoneSnapshot(
    bool Enabled,
    bool Running,
    string LocalName,
    IReadOnlyList<PhoneDevice> Devices,
    PhoneDevice? Active,
    PhoneBattery? Battery,
    PhoneSignal? Signal,
    IReadOnlyList<PhoneNotification> Notifications,
    PhoneCall? Call = null);

public sealed record SignalBar(double Height, bool On);

public sealed partial class DeviceItem(PhoneViewModel owner, PhoneDevice device) : ObservableObject
{
    public PhoneDevice Device { get; } = device;
    public string Name => Device.Name;
    public string Icon => Device.DeviceType switch
    {
        "tablet" => "tablet_android",
        "desktop" => "desktop_windows",
        "laptop" => "laptop",
        "tv" => "tv",
        _ => "smartphone",
    };
    public string? Address => Device.Address;

    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private Task Pair() => owner.PairAsync(this);
}

public sealed partial class NotificationItem : ObservableObject
{
    private readonly PhoneViewModel _owner;

    internal NotificationItem(PhoneViewModel owner, PhoneNotification model)
    {
        _owner = owner;
        _model = model;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Id), nameof(AppName), nameof(Title), nameof(Text), nameof(Time), nameof(IconPng),
        nameof(CanReply), nameof(CanDismiss), nameof(Actions), nameof(HasActions), nameof(Otp), nameof(HasOtp), nameof(OtpLabel))]
    private PhoneNotification _model;

    private string? _otp;
    private PhoneNotification? _otpFor;

    /// <summary>One-time code found in the title / text (settings Phone.OtpCopy), digits only.</summary>
    public string? Otp
    {
        get
        {
            if (!_owner.OtpEnabled) return null;
            if (!ReferenceEquals(_otpFor, Model))
            {
                _otpFor = Model;
                _otp = OtpDetector.Find(Model.Title, Model.Text);
            }
            return _otp;
        }
    }

    public bool HasOtp => Otp is not null;

    /// <summary>Code grouped for reading ("482 913").</summary>
    public string OtpLabel => Otp is { Length: >= 6 } c && c.Length % 3 == 0
        ? string.Join(" ", Enumerable.Range(0, c.Length / 3).Select(i => c.Substring(i * 3, 3)))
        : Otp ?? "";

    [ObservableProperty] private bool _otpCopied;

    [RelayCommand]
    private void CopyOtp()
    {
        if (Otp is not { } code) return;
        _owner.CopyCode(code);
        OtpCopied = true;
        ResetCopiedAsync().Forget("phone.otpCopied");
    }

    private async Task ResetCopiedAsync()
    {
        await Task.Delay(1800);
        OtpCopied = false;
    }

    internal void RefreshOtp()
    {
        _otpFor = null;
        OnPropertyChanged(nameof(Otp));
        OnPropertyChanged(nameof(HasOtp));
        OnPropertyChanged(nameof(OtpLabel));
    }

    public string Id => Model.Id;
    public string AppName => Model.AppName;
    public string Title => string.IsNullOrWhiteSpace(Model.Title) ? Model.AppName : Model.Title;
    public string Text => string.IsNullOrWhiteSpace(Model.Text) ? Model.Ticker ?? "" : Model.Text;
    public DateTimeOffset Time => Model.Time;
    public byte[]? IconPng => Model.IconPng;
    public bool CanReply => Model.ReplyId is not null;
    public bool CanDismiss => Model.IsClearable;
    public IReadOnlyList<string> Actions => Model.Actions;
    public bool HasActions => Model.Actions.Count > 0;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isRemoving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendReplyCommand))]
    private string _replyText = "";

    [RelayCommand]
    private void Toggle() => _owner.ToggleExpanded(this);

    [RelayCommand]
    private Task Dismiss() => _owner.DismissAsync(this);

    [RelayCommand]
    private Task InvokeAction(string? action) => action is null ? Task.CompletedTask : _owner.InvokeActionAsync(this, action);

    private bool CanSendReply() => !string.IsNullOrWhiteSpace(ReplyText);

    [RelayCommand(CanExecute = nameof(CanSendReply))]
    private async Task SendReply()
    {
        var text = ReplyText.Trim();
        ReplyText = "";
        await _owner.ReplyAsync(this, text);
    }
}

public sealed partial class PhoneViewModel : ObservableObject
{
    private static IPhoneService? Service => AppServices.Phone;

    private PhoneSnapshot? _override;
    private bool _subscribed;
    private CancellationTokenSource? _statusCts;
    private System.Windows.Threading.DispatcherTimer? _callTimer;
    private PhoneCall? _call;
    private PhoneCall? _mutedCall;
    private bool _lastOtpEnabled = true;

    public PhoneViewModel() => Refresh();

    public ObservableCollection<DeviceItem> Discovered { get; } = [];
    public ObservableCollection<NotificationItem> Notifications { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaired), nameof(IsOffline), nameof(IsConnected), nameof(IsPairing))]
    private PhoneMode _mode;

    public bool IsPaired => Mode is PhoneMode.Connected or PhoneMode.Offline;
    public bool IsPairing => Mode is PhoneMode.PairOutgoing or PhoneMode.PairIncoming;
    public bool IsConnected => Mode == PhoneMode.Connected;
    public bool IsOffline => Mode == PhoneMode.Offline;
    [ObservableProperty] private string _localName = "";
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _deviceIcon = "smartphone";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _verificationKey;
    [ObservableProperty] private string? _pairDeviceId;
    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private double _batteryValue;
    [ObservableProperty] private string _batteryText = "";
    [ObservableProperty] private bool _isCharging;
    [ObservableProperty] private BatteryTone _batteryTone;
    [ObservableProperty] private bool _hasSignal;
    [ObservableProperty] private string _networkType = "";
    [ObservableProperty] private IReadOnlyList<SignalBar> _signalBars = [];
    [ObservableProperty] private int _notificationCount;
    [ObservableProperty] private bool _hasClearable;
    [ObservableProperty] private bool _isOverflowOpen;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string? _feedback;
    [ObservableProperty] private string _batteryCaption = "";

    // ---- call status ----
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCallMissed))] private bool _hasCall;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCallMissed))] private PhoneCallState _callState;
    [ObservableProperty] private string _callName = "";
    [ObservableProperty] private string? _callNumber;
    [ObservableProperty] private string _callStatusText = "";
    [ObservableProperty] private byte[]? _callPhoto;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanMute))] private bool _isCallRinging;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanMute))] private bool _isRingerMuted;
    public bool CanMute => IsCallRinging && !IsRingerMuted;
    [ObservableProperty] private bool _callHasName;
    /// <summary>Notification list height: shorter while the call card takes space above it.</summary>
    [ObservableProperty] private double _notificationsMaxHeight = 420;
    public bool IsCallMissed => HasCall && CallState == PhoneCallState.Missed;

    internal bool OtpEnabled => AppServices.Settings?.Current.Phone.OtpCopy ?? true;

    public bool HasDiscovered => Discovered.Count > 0;

    public void OnShown()
    {
        if (!_subscribed && Service is { } s)
        {
            s.Changed += OnServiceChanged;
            s.PairRequested += OnPairRequested;
            AppServices.Settings.Changed += OnServiceChanged;
            _subscribed = true;
        }
        Refresh();
        if (Mode == PhoneMode.Setup) Service?.RefreshAsync().Forget("phone.refresh");
    }

    public void OnHidden()
    {
        IsOverflowOpen = false;
        StopCallTimer();
        if (_subscribed && Service is { } s)
        {
            s.Changed -= OnServiceChanged;
            s.PairRequested -= OnPairRequested;
            AppServices.Settings.Changed -= OnServiceChanged;
            _subscribed = false;
        }
    }

    private void OnServiceChanged(object? sender, EventArgs e) => AppServices.Ui.Post(Refresh);
    private void OnPairRequested(object? sender, PhoneDevice e) => AppServices.Ui.Post(Refresh);

    /// <summary>Preview hook: freezes the view model on a fake snapshot.</summary>
    internal void ApplyOverride(PhoneSnapshot snapshot)
    {
        _override = snapshot;
        Refresh();
    }

    private PhoneSnapshot Capture()
    {
        if (_override is not null) return _override;
        var s = Service;
        var enabled = AppServices.Settings?.Current.Phone.Enabled ?? true;
        if (s is null) return new PhoneSnapshot(false, false, "", [], null, null, null, []);
        return new PhoneSnapshot(enabled, s.IsRunning, s.LocalDeviceName, s.Devices, s.ActiveDevice, s.Battery, s.Signal, s.Notifications, s.ActiveCall);
    }

    private void Refresh()
    {
        var snap = Capture();
        LocalName = snap.LocalName;

        var incoming = snap.Devices.FirstOrDefault(d => d.PairState == PhonePairState.RequestedByPeer);
        var outgoing = snap.Devices.FirstOrDefault(d => d.PairState == PhonePairState.RequestedByUs);

        PhoneMode mode;
        PhoneDevice? focus = null;
        if (!snap.Enabled || !snap.Running) mode = PhoneMode.Disabled;
        else if (incoming is not null) { mode = PhoneMode.PairIncoming; focus = incoming; }
        else if (outgoing is not null) { mode = PhoneMode.PairOutgoing; focus = outgoing; }
        else if (snap.Active is { } a) { mode = a.IsReachable ? PhoneMode.Connected : PhoneMode.Offline; focus = a; }
        else mode = PhoneMode.Setup;

        if (mode != PhoneMode.Connected && mode != PhoneMode.Offline) IsOverflowOpen = false;
        Mode = mode;

        DeviceName = focus?.Name ?? "";
        DeviceIcon = focus is null ? "smartphone" : new DeviceItem(this, focus).Icon;
        VerificationKey = FormatKey(focus?.VerificationKey);
        PairDeviceId = focus?.Id;
        StatusText = mode switch
        {
            PhoneMode.Connected => Loc.Get("phone.statusConnected"),
            PhoneMode.Offline => Loc.Get("phone.statusOffline"),
            _ => "",
        };

        // Battery (the service keeps the last value through a short link hand-over, so show it while offline too)
        var bat = mode is PhoneMode.Connected or PhoneMode.Offline ? snap.Battery : null;
        BatteryCaption = Loc.Get(mode == PhoneMode.Offline ? "phone.batteryLastKnown" : "phone.battery");
        HasBattery = bat is not null;
        if (bat is not null)
        {
            BatteryValue = Math.Clamp(bat.Percent, 0, 100) / 100.0;
            BatteryText = bat.Percent + "%";
            IsCharging = bat.IsCharging;
            BatteryTone = bat.IsLow ? BatteryTone.Low : bat.Percent <= 20 ? BatteryTone.Warning : BatteryTone.Normal;
        }
        else
        {
            BatteryValue = 0;
            BatteryText = "–";
            IsCharging = false;
            BatteryTone = BatteryTone.Normal;
        }

        // Signal
        var sig = mode == PhoneMode.Connected ? snap.Signal : null;
        HasSignal = sig is not null;
        NetworkType = sig is null || sig.NetworkType == "Unknown" ? "" : sig.NetworkType;
        var strength = sig?.Strength ?? 0;
        SignalBars = [new(5, strength >= 1), new(8, strength >= 2), new(11, strength >= 3), new(14, strength >= 4)];

        // Discovered (unpaired) devices
        var unpaired = mode == PhoneMode.Setup
            ? snap.Devices.Where(d => d.PairState == PhonePairState.NotPaired && d.IsReachable).ToList()
            : [];
        SyncDevices(unpaired);

        // Notifications
        SyncNotifications(mode == PhoneMode.Connected ? snap.Notifications : []);

        // Call
        var callEnabled = AppServices.Settings?.Current.Phone.CallStatus ?? true;
        UpdateCall(callEnabled && mode is PhoneMode.Connected or PhoneMode.Offline ? snap.Call : null);
    }

    private void UpdateCall(PhoneCall? call)
    {
        _call = call;
        HasCall = call is not null;
        NotificationsMaxHeight = call is null ? 420 : 330;
        if (call is null)
        {
            _mutedCall = null;
            IsRingerMuted = false;
            IsCallRinging = false;
            StopCallTimer();
            return;
        }
        CallState = call.State;
        IsCallRinging = call.State == PhoneCallState.Ringing;
        if (_mutedCall is not null && (_mutedCall.Since != call.Since || call.State != PhoneCallState.Ringing)) _mutedCall = null;
        IsRingerMuted = _mutedCall is not null;
        CallName = call.ContactName ?? call.PhoneNumber ?? Loc.Get("phone.callUnknown");
        CallNumber = call.PhoneNumber is { } n && n != CallName ? n : null;
        CallPhoto = call.ContactPhoto;
        CallHasName = call.ContactName is { } nm && nm != call.PhoneNumber;
        UpdateCallStatus();
        if (call.State == PhoneCallState.Talking && _subscribed) StartCallTimer();
        else StopCallTimer();
    }

    private void UpdateCallStatus()
    {
        if (_call is not { } c) return;
        CallStatusText = c.State switch
        {
            PhoneCallState.Ringing => Loc.Get("phone.callRinging"),
            PhoneCallState.Talking => Loc.Get("phone.callTalking") + " • " + FormatDuration(DateTimeOffset.Now - c.Since),
            PhoneCallState.Missed => Loc.Get("phone.callMissed") + " • " + c.Since.ToLocalTime().ToString("HH:mm"),
            _ => "",
        };
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{d.Minutes:00}:{d.Seconds:00}";
    }

    /// <summary>1 s ticker for the call duration; runs only while the tab is visible and a call is in progress.</summary>
    private void StartCallTimer()
    {
        if (_callTimer is null)
        {
            _callTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _callTimer.Tick += (_, _) => UpdateCallStatus();
        }
        if (!_callTimer.IsEnabled) _callTimer.Start();
    }

    private void StopCallTimer() => _callTimer?.Stop();

    [RelayCommand]
    private async Task MuteRinger()
    {
        if (_call is not { State: PhoneCallState.Ringing } call || IsRingerMuted) return;
        _mutedCall = call;
        IsRingerMuted = true;
        if (_override is null && Service is { } s) await s.MuteRingerAsync();
    }

    internal void CopyCode(string code)
    {
        try
        {
            if (AppServices.Clipboard is { } c) c.AddExternal(code, ClipOrigin.Phone, setSystemClipboard: true);
            else System.Windows.Clipboard.SetText(code);
            ShowFeedback(Loc.Get("phone.codeCopied"));
        }
        catch (Exception ex) { Log.Warn("Phone: copying the code failed", ex); }
    }

    private static string? FormatKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.ToUpperInvariant();
        return key.Length == 8 ? key[..4] + " " + key[4..] : key;
    }

    private void SyncDevices(IReadOnlyList<PhoneDevice> devices)
    {
        for (var i = Discovered.Count - 1; i >= 0; i--)
            if (devices.All(d => d.Id != Discovered[i].Device.Id)) Discovered.RemoveAt(i);
        for (var i = 0; i < devices.Count; i++)
        {
            var existing = Discovered.FirstOrDefault(x => x.Device.Id == devices[i].Id);
            if (existing is not null && existing.Device == devices[i]) continue;
            if (existing is not null) Discovered.Remove(existing);
            Discovered.Insert(Math.Min(i, Discovered.Count), new DeviceItem(this, devices[i]));
        }
        OnPropertyChanged(nameof(HasDiscovered));
    }

    private void SyncNotifications(IReadOnlyList<PhoneNotification> list)
    {
        var otpChanged = _lastOtpEnabled != OtpEnabled;
        _lastOtpEnabled = OtpEnabled;
        foreach (var item in Notifications.ToList())
            if (!item.IsRemoving && list.All(n => n.Id != item.Id)) RemoveAnimated(item);

        for (var i = 0; i < list.Count; i++)
        {
            var n = list[i];
            var existing = Notifications.FirstOrDefault(x => x.Id == n.Id && !x.IsRemoving);
            if (existing is not null) { existing.Model = n; if (otpChanged) existing.RefreshOtp(); continue; }
            Notifications.Insert(Math.Min(i, Notifications.Count), new NotificationItem(this, n));
        }
        NotificationCount = list.Count;
        HasClearable = list.Any(n => n.IsClearable);
    }

    private void RemoveAnimated(NotificationItem item)
    {
        if (item.IsRemoving) return;
        item.IsRemoving = true;
        RemoveLaterAsync(item).Forget("phone.removeRow");
    }

    private async Task RemoveLaterAsync(NotificationItem item)
    {
        await Task.Delay(UI.Animation.Motion.ReduceMotion ? 0 : 260);
        Notifications.Remove(item);
    }

    // ---- notification actions ----

    internal void ToggleExpanded(NotificationItem item)
    {
        var expand = !item.IsExpanded;
        foreach (var n in Notifications) if (n != item) n.IsExpanded = false;
        item.IsExpanded = expand;
    }

    internal async Task DismissAsync(NotificationItem item)
    {
        RemoveAnimated(item);
        NotificationCount = Math.Max(0, Notifications.Count(n => !n.IsRemoving));
        HasClearable = Notifications.Any(n => !n.IsRemoving && n.CanDismiss);
        if (_override is null && Service is { } s) await s.DismissNotificationAsync(item.Id);
    }

    internal async Task InvokeActionAsync(NotificationItem item, string action)
    {
        if (_override is null && Service is { } s) await s.InvokeNotificationActionAsync(item.Id, action);
    }

    internal async Task ReplyAsync(NotificationItem item, string text)
    {
        if (item.Model.ReplyId is not { } replyId) return;
        if (_override is null && Service is { } s) await s.ReplyToNotificationAsync(replyId, text);
        ShowFeedback(Loc.Get("phone.replySent"));
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        var targets = Notifications.Where(n => n.CanDismiss && !n.IsRemoving).ToList();
        foreach (var t in targets) await DismissAsync(t);
    }

    // ---- device actions ----

    [RelayCommand]
    private async Task Ring()
    {
        if (Service is { } s && _override is null) await s.RingAsync();
        ShowFeedback(Loc.Get("phone.ringSent"));
    }

    [RelayCommand]
    private async Task Ping()
    {
        if (Service is { } s && _override is null) await s.PingAsync();
        ShowFeedback(Loc.Get("phone.pingSent"));
    }

    [RelayCommand]
    private async Task SendClipboard()
    {
        var text = AppServices.Clipboard is { } c ? await c.GetSystemClipboardTextAsync() : null;
        if (string.IsNullOrEmpty(text))
        {
            ShowFeedback(Loc.Get("phone.clipboardEmpty"));
            return;
        }
        if (Service is { } s && _override is null) await s.SendClipboardAsync(text);
        ShowFeedback(Loc.Get("phone.clipboardSent"));
    }

    [RelayCommand]
    private async Task RefreshDevices()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            if (Service is { } s && _override is null)
            {
                await s.RefreshAsync();
                if (Mode == PhoneMode.Connected) await s.RequestNotificationsAsync();
            }
            await Task.Delay(900);
        }
        finally { IsRefreshing = false; }
    }

    internal async Task PairAsync(DeviceItem item)
    {
        if (item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            if (Service is { } s && _override is null) await s.RequestPairAsync(item.Device.Id);
        }
        finally { item.IsBusy = false; }
    }

    [RelayCommand]
    private async Task CancelPair()
    {
        if (PairDeviceId is { } id && Service is { } s && _override is null) await s.UnpairAsync(id);
    }

    [RelayCommand]
    private async Task AcceptPair()
    {
        if (PairDeviceId is { } id && Service is { } s && _override is null) await s.AcceptPairAsync(id);
    }

    [RelayCommand]
    private async Task RejectPair()
    {
        if (PairDeviceId is { } id && Service is { } s && _override is null) await s.RejectPairAsync(id);
    }

    [RelayCommand]
    private void ToggleOverflow() => IsOverflowOpen = !IsOverflowOpen;

    [RelayCommand]
    private async Task Unpair()
    {
        IsOverflowOpen = false;
        if (PairDeviceId is { } id && Service is { } s && _override is null) await s.UnpairAsync(id);
    }

    [RelayCommand]
    private void Enable()
    {
        if (AppServices.Settings is { } st && !st.Current.Phone.Enabled)
        {
            st.Update(x => x.Phone.Enabled = true);
        }
        else AppServices.Shell?.Expand(ShellTab.Settings);
    }

    [RelayCommand]
    private void OpenSettings() => AppServices.Shell?.Expand(ShellTab.Settings);

    private void ShowFeedback(string text)
    {
        _statusCts?.Cancel();
        var cts = _statusCts = new CancellationTokenSource();
        Feedback = text;
        ClearFeedbackAsync(cts.Token).Forget("phone.feedback");
    }

    private async Task ClearFeedbackAsync(CancellationToken ct)
    {
        try { await Task.Delay(2200, ct); }
        catch (OperationCanceledException) { return; }
        Feedback = null;
    }
}
