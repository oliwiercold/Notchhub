namespace NotchHub.Core.Contracts;

public enum PhonePairState { NotPaired, RequestedByUs, RequestedByPeer, Paired }

/// <summary>A KDE Connect device (normally a phone) — discovered and/or paired.</summary>
/// <param name="DeviceType">"phone", "tablet", "desktop", "laptop", "tv".</param>
/// <param name="VerificationKey">8 hex chars both sides show while pairing; null if unknown.</param>
public sealed record PhoneDevice(
    string Id,
    string Name,
    string DeviceType,
    bool IsReachable,
    PhonePairState PairState,
    string? Address,
    string? VerificationKey,
    int ProtocolVersion);

public sealed record PhoneBattery(int Percent, bool IsCharging, bool IsLow);

/// <param name="NetworkType">e.g. "5G", "LTE", "HSPA", "GSM", "Unknown".</param>
/// <param name="Strength">0..4 bars.</param>
public sealed record PhoneSignal(string NetworkType, int Strength);

/// <param name="Id">Phone-side notification key.</param>
/// <param name="ReplyId">requestReplyId when the notification supports inline reply.</param>
/// <param name="IconPng">App/notification icon downloaded from the phone (PNG bytes) or null.</param>
public sealed record PhoneNotification(
    string Id,
    string AppName,
    string Title,
    string Text,
    string? Ticker,
    DateTimeOffset Time,
    bool IsClearable,
    bool IsSilent,
    string? ReplyId,
    IReadOnlyList<string> Actions,
    byte[]? IconPng);

public enum PhoneCallState { Ringing, Talking, Missed, Ended }

/// <summary>Telephony state reported by the phone (KDE Connect telephony plugin).</summary>
public sealed record PhoneCall(PhoneCallState State, string? ContactName, string? PhoneNumber, DateTimeOffset Since, byte[]? ContactPhoto);

public sealed class PhoneNotificationEventArgs(PhoneNotification notification) : EventArgs
{
    public PhoneNotification Notification { get; } = notification;
}

/// <summary>
/// Built-in KDE Connect protocol endpoint (this PC appears as a KDE Connect device; the phone pairs with it).
/// Provides phone battery, signal, notifications (read / dismiss / reply / actions), ring, ping and clipboard exchange.
/// </summary>
public interface IPhoneService : IBackgroundService
{
    /// <summary>Endpoint running (settings Phone.Enabled and network listener up).</summary>
    bool IsRunning { get; }
    string LocalDeviceId { get; }
    string LocalDeviceName { get; }
    /// <summary>Discovered (reachable) and paired (possibly offline) devices.</summary>
    IReadOnlyList<PhoneDevice> Devices { get; }
    /// <summary>First paired + reachable device, else first paired device, else null.</summary>
    PhoneDevice? ActiveDevice { get; }
    /// <summary>Battery of <see cref="ActiveDevice"/> (null when unknown).</summary>
    PhoneBattery? Battery { get; }
    PhoneSignal? Signal { get; }
    /// <summary>Active notifications of <see cref="ActiveDevice"/>, newest first.</summary>
    IReadOnlyList<PhoneNotification> Notifications { get; }
    /// <summary>Current call (ringing / talking) of <see cref="ActiveDevice"/>, null when none. A missed call stays ~1 minute.</summary>
    PhoneCall? ActiveCall { get; }

    /// <summary>Anything above changed.</summary>
    event EventHandler? Changed;
    /// <summary>A new, non-silent notification arrived (for the notch peek).</summary>
    event EventHandler<PhoneNotificationEventArgs>? NotificationPosted;
    /// <summary>Text clipboard content received from the phone.</summary>
    event EventHandler<string>? ClipboardReceived;
    /// <summary>A device asks to pair; answer with <see cref="AcceptPairAsync"/> / <see cref="RejectPairAsync"/>.</summary>
    event EventHandler<PhoneDevice>? PairRequested;

    /// <summary>Re-announces this PC on the network (UDP broadcast) to speed up discovery.</summary>
    Task RefreshAsync();
    Task<bool> RequestPairAsync(string deviceId);
    Task AcceptPairAsync(string deviceId);
    Task RejectPairAsync(string deviceId);
    Task UnpairAsync(string deviceId);
    /// <summary>Makes the active phone ring (find my phone).</summary>
    Task RingAsync();
    Task PingAsync(string? message = null);
    Task SendClipboardAsync(string text);
    Task DismissNotificationAsync(string notificationId);
    Task ReplyToNotificationAsync(string replyId, string message);
    Task InvokeNotificationActionAsync(string notificationId, string action);
    /// <summary>Asks the phone to (re)send all its current notifications.</summary>
    Task RequestNotificationsAsync();
    /// <summary>Silences the ringing phone (kdeconnect.telephony.request_mute).</summary>
    Task MuteRingerAsync();
}
