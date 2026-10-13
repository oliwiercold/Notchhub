using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.UI.Converters;
using NotchHub.UI.Localization;

namespace NotchHub.Features.Compact;

public sealed partial class HudViewModel : ObservableObject
{
    private static readonly TimeSpan LevelDuration = TimeSpan.FromSeconds(1.6);
    private bool _applying;
    private int? _pendingBrightness;
    private bool _brightnessBusy;
    private string? _pairDeviceId;
    private readonly DispatcherTimer _callTick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private PhoneCall? _call;
    private bool _shown;

    [ObservableProperty] private HudKind _kind = HudKind.Info;

    // ── volume / brightness ──
    [ObservableProperty] private double _level;
    [ObservableProperty] private string _levelGlyph = "volume_up";
    [ObservableProperty] private string _levelText = "";
    [ObservableProperty] private string? _levelLabel;
    [ObservableProperty] private bool _isDragging;

    // ── phone notification ──
    [ObservableProperty] private string _notificationApp = "";
    [ObservableProperty] private string _notificationTitle = "";
    [ObservableProperty] private string _notificationText = "";
    [ObservableProperty] private byte[]? _notificationIcon;
    [ObservableProperty] private DateTimeOffset _notificationTime;

    [ObservableProperty] private string? _otpCode;
    [ObservableProperty] private string _otpButtonText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(OtpIcon))] private bool _otpCopied;
    public string OtpIcon => OtpCopied ? "check" : "content_copy";

    // ── phone charging ──
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ChargePercentText))] private int _chargePercent;
    public string ChargePercentText => $"{ChargePercent}%";
    [ObservableProperty] private bool _isCharging;
    [ObservableProperty] private bool _isBatteryLow;
    [ObservableProperty] private string _chargeTitle = "";
    [ObservableProperty] private string _chargeGlyph = "bolt";

    // ── phone call ──
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CallGlyph))] private PhoneCallState _callState;
    public string CallGlyph => CallState switch
    {
        PhoneCallState.Ringing => "ring_volume",
        PhoneCallState.Missed => "phone_missed",
        PhoneCallState.Ended => "call_end",
        _ => "call",
    };
    [ObservableProperty] private bool _isRinging;
    [ObservableProperty] private string _callName = "";
    [ObservableProperty] private string _callDetail = "";
    [ObservableProperty] private string _callStatusText = "";
    [ObservableProperty] private byte[]? _callPhoto;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MuteIcon), nameof(MuteText))] private bool _ringerMuted;
    public string MuteIcon => RingerMuted ? "check" : "volume_off";
    public string MuteText => Loc.Get(RingerMuted ? "compact.muted" : "compact.mute");

    // ── pair request ──
    [ObservableProperty] private string _pairDeviceName = "";
    [ObservableProperty] private string _pairKey = "";

    // ── info ──
    [ObservableProperty] private string _infoText = "";

    /// <summary>Raised (UI thread) when the presented kind changes or is re-presented — the view animates the transition.</summary>
    public event EventHandler<bool>? Presented;

    public HudViewModel() => _callTick.Tick += (_, _) => UpdateCallStatus();

    /// <summary>Called by the shell (UI thread) right before the HUD becomes visible.</summary>
    public void Present(HudRequest request)
    {
        var kindChanged = request.Kind != Kind;
        switch (request.Kind)
        {
            case HudKind.Volume:
            case HudKind.Brightness:
                var lvl = request.Payload as HudLevel;
                if (lvl is null)
                {
                    lvl = request.Kind == HudKind.Volume
                        ? new HudLevel(AppServices.Audio?.Volume ?? 0, AppServices.Audio?.IsMuted ?? false)
                        : new HudLevel((AppServices.Display?.Brightness ?? 0) / 100.0);
                }
                LevelLabel = lvl.Label;
                Kind = request.Kind;
                if (!IsDragging) SetLevel(lvl.Level * 100, lvl.Muted);
                break;
            case HudKind.PhoneNotification when request.Payload is PhoneNotification n:
                NotificationApp = n.AppName;
                NotificationTitle = n.Title;
                NotificationText = n.Text;
                NotificationIcon = n.IconPng;
                NotificationTime = n.Time;
                OtpCopied = false;
                OtpCode = AppServices.Settings?.Current.Phone.OtpCopy != false ? OtpDetector.Find(n.Title, n.Text, n.Ticker) : null;
                OtpButtonText = OtpCode is { } code ? Loc.Format("compact.copyCode", code) : "";
                Kind = request.Kind;
                break;
            case HudKind.PhoneCharging when request.Payload is PhoneBattery b:
                ChargePercent = Math.Clamp(b.Percent, 0, 100);
                IsCharging = b.IsCharging;
                IsBatteryLow = b.IsLow || b.Percent <= 15;
                ChargeTitle = Loc.Get(b.IsCharging ? "compact.charging" : "compact.unplugged");
                ChargeGlyph = b.IsCharging ? "bolt" : "power_off";
                Kind = request.Kind;
                break;
            case HudKind.PhoneCall when request.Payload is PhoneCall call:
                if (!kindChanged && _call?.State != call.State) kindChanged = true; // ringing → talking animates too
                if (_call is null || _call.State != call.State || _call.Since != call.Since) RingerMuted = false;
                _call = call;
                CallState = call.State;
                IsRinging = call.State == PhoneCallState.Ringing;
                var name = string.IsNullOrWhiteSpace(call.ContactName) ? null : call.ContactName.Trim();
                var number = string.IsNullOrWhiteSpace(call.PhoneNumber) ? null : call.PhoneNumber.Trim();
                CallName = name ?? number ?? Loc.Get("compact.unknownCaller");
                CallDetail = name is not null && number is not null ? number : "";
                CallPhoto = call.ContactPhoto;
                Kind = request.Kind;
                UpdateCallStatus();
                break;
            case HudKind.PhonePairRequest when request.Payload is PhoneDevice d:
                _pairDeviceId = d.Id;
                PairDeviceName = d.Name;
                PairKey = FormatKey(d.VerificationKey);
                Kind = request.Kind;
                break;
            case HudKind.TimerFinished:
                Kind = request.Kind;
                break;
            default:
                InfoText = request.Payload as string ?? "";
                Kind = HudKind.Info;
                break;
        }
        UpdateCallTimer();
        Presented?.Invoke(this, kindChanged);
    }

    private void UpdateCallStatus()
    {
        if (_call is not { } c) return;
        CallStatusText = c.State switch
        {
            PhoneCallState.Ringing => Loc.Get("compact.incomingCall"),
            PhoneCallState.Talking => TimeSpanClock.ToClock(DateTimeOffset.Now - c.Since is var d && d > TimeSpan.Zero ? d : TimeSpan.Zero),
            PhoneCallState.Missed => Loc.Get("compact.missedCall"),
            _ => Loc.Get("compact.callEnded"),
        };
    }

    /// <summary>Live call duration ticks once per second, only while the HUD shows a call in progress.</summary>
    private void UpdateCallTimer()
    {
        if (_shown && Kind == HudKind.PhoneCall && CallState == PhoneCallState.Talking) { if (!_callTick.IsEnabled) _callTick.Start(); }
        else _callTick.Stop();
    }

    private static string FormatKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "—";
        // "8F3A21C4" → "8F3A 21C4" (easier to compare with the phone).
        return key.Length > 4 && !key.Contains(' ') ? string.Join(" ", key.Chunk(4).Select(c => new string(c))) : key;
    }

    private void SetLevel(double percent, bool muted)
    {
        _applying = true;
        try { Level = Math.Clamp(percent, 0, 100); }
        finally { _applying = false; }
        UpdateLevelVisuals(muted);
    }

    private void UpdateLevelVisuals(bool muted)
    {
        var p = (int)Math.Round(Level);
        LevelText = $"{p}%";
        LevelGlyph = Kind == HudKind.Brightness
            ? (p < 34 ? "brightness_low" : p < 67 ? "brightness_medium" : "brightness_high")
            : muted || p == 0 ? "volume_off" : p < 50 ? "volume_down" : "volume_up";
    }

    partial void OnLevelChanged(double value)
    {
        if (_applying) return;
        // User is dragging / scrolling the slider → apply to the system.
        if (Kind == HudKind.Volume && AppServices.Audio is { IsAvailable: true } audio)
        {
            audio.SetVolume((float)(value / 100.0));
            if (audio.IsMuted && value > 0) audio.SetMuted(false);
            UpdateLevelVisuals(false);
        }
        else if (Kind == HudKind.Brightness && AppServices.Display is { BrightnessSupported: true })
        {
            PushBrightness((int)Math.Round(value));
            UpdateLevelVisuals(false);
        }
    }

    /// <summary>DDC/CI is slow: keep at most one request in flight and only send the latest value.</summary>
    private void PushBrightness(int percent)
    {
        _pendingBrightness = percent;
        if (_brightnessBusy) return;
        _brightnessBusy = true;
        Loop().Forget("hud brightness");

        async Task Loop()
        {
            try
            {
                while (_pendingBrightness is int p)
                {
                    _pendingBrightness = null;
                    await AppServices.Display.SetBrightnessAsync(p);
                }
            }
            finally { _brightnessBusy = false; }
        }
    }

    /// <summary>Keeps the level HUD on screen while the user drags its slider.</summary>
    public void BeginDrag()
    {
        IsDragging = true;
        AppServices.Shell?.ShowHud(new HudRequest(Kind, new HudLevel(Level / 100.0, false, LevelLabel), TimeSpan.FromMinutes(5)));
    }

    public void EndDrag()
    {
        IsDragging = false;
        AppServices.Shell?.ShowHud(new HudRequest(Kind, new HudLevel(Level / 100.0, false, LevelLabel), LevelDuration));
    }

    [RelayCommand] private void OpenPhone() => AppServices.Shell?.Expand(ShellTab.Phone);

    [RelayCommand]
    private void CopyOtp()
    {
        if (OtpCode is not { } code) return;
        try { AppServices.Clipboard?.AddExternal(code, ClipOrigin.Phone, true); }
        catch (Exception ex) { Log.Warn("HUD: copy code failed", ex); return; }
        OtpCopied = true;
        OtpButtonText = Loc.Get("compact.copied");
    }

    [RelayCommand]
    private void MuteRinger()
    {
        if (RingerMuted) return;
        RingerMuted = true;
        AppServices.Phone?.MuteRingerAsync().Forget("hud mute ringer");
    }

    [RelayCommand]
    private void AcceptPair()
    {
        if (_pairDeviceId is { } id) AppServices.Phone.AcceptPairAsync(id).Forget("hud accept pair");
        AppServices.Shell?.Collapse();
    }

    [RelayCommand]
    private void RejectPair()
    {
        if (_pairDeviceId is { } id) AppServices.Phone.RejectPairAsync(id).Forget("hud reject pair");
        AppServices.Shell?.Collapse();
    }

    [RelayCommand]
    private void AddMinute()
    {
        AppServices.Timer.Start(TimeSpan.FromMinutes(1));
        AppServices.Shell?.Collapse();
    }

    [RelayCommand]
    private void StopTimer()
    {
        AppServices.Timer.Reset();
        AppServices.Shell?.Collapse();
    }



    public void OnShown() { _shown = true; UpdateCallStatus(); UpdateCallTimer(); }

    public void OnHidden()
    {
        _shown = false;
        IsDragging = false;
        UpdateCallTimer();
    }
}
