using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo KDE Connect endpoint: a paired, reachable "Pixel 8 Pro" (76 % charging, 5G 3 bars) with five notifications.
/// Dismiss / reply / actions work in memory; with <see cref="DemoServices.LiveEvents"/> a new notification arrives every ~45 s,
/// a phone call rings every ~3 min (answered or missed) and the charger is plugged / unplugged every ~2 min. No network traffic.
/// </summary>
public sealed class FakePhoneService : FakeBackgroundService, IPhoneService
{
    private const string PhoneId = "demo_pixel_8_pro";
    private static readonly (string App, string Title, string Text, IconSpec Icon, bool Reply)[] Incoming =
    [
        ("WhatsApp", "Mama", "Zadzwonisz wieczorem? 😊", DemoIcons.PhoneWhatsApp, true),
        ("Allegro", "Paczka w drodze", "Twoja przesyłka dotrze jutro do Paczkomatu WAW12M.", DemoIcons.PhoneAllegro, false),
        ("Instagram", "arsh.codes", "liked your photo.", DemoIcons.PhoneInstagram, false),
        ("Slack", "#notchhub", "Mainak: the new HUD looks amazing 🔥", DemoIcons.PhoneSlack, true),
        ("Weather", "Rain expected", "Light rain in Warszawa starting around 17:00.", DemoIcons.PhoneWeather, false),
    ];

    private readonly List<PhoneNotification> _notifications = [];
    private PhoneDevice _device = new(PhoneId, "Pixel 8 Pro", "phone", true, PhonePairState.Paired, "192.168.1.42", "3F9A21C7", 8);
    private PhoneBattery _battery = new(76, true, false);
    private int _nextIncoming;
    private PhoneCall? _call;
    private int _callGen;
    private int _nextCaller;
    private static readonly (string? Name, string Number)[] Callers =
    [
        ("Mama", "+48 600 100 200"),
        (null, "+48 22 123 45 67"),
        ("Arsh", "+48 512 345 678"),
    ];

    /// <inheritdoc />
    public bool IsRunning => true;

    /// <inheritdoc />
    public string LocalDeviceId => "notchhub_demo_pc";

    /// <inheritdoc />
    public string LocalDeviceName => Environment.MachineName;

    /// <inheritdoc />
    public IReadOnlyList<PhoneDevice> Devices
    {
        get { lock (Gate) return [_device]; }
    }

    /// <inheritdoc />
    public PhoneDevice? ActiveDevice
    {
        get { lock (Gate) return _device.PairState == PhonePairState.Paired ? _device : null; }
    }

    /// <inheritdoc />
    public PhoneBattery? Battery
    {
        get { lock (Gate) return ActiveLocked ? _battery : null; }
    }

    /// <inheritdoc />
    public PhoneSignal? Signal
    {
        get { lock (Gate) return ActiveLocked ? new PhoneSignal("5G", 3) : null; }
    }

    /// <inheritdoc />
    public IReadOnlyList<PhoneNotification> Notifications
    {
        get { lock (Gate) return ActiveLocked ? _notifications.ToArray() : []; }
    }

    /// <inheritdoc />
    public PhoneCall? ActiveCall
    {
        get { lock (Gate) return ActiveLocked ? _call : null; }
    }

    private bool ActiveLocked => _device.PairState == PhonePairState.Paired;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<PhoneNotificationEventArgs>? NotificationPosted;

    /// <inheritdoc />
    public event EventHandler<string>? ClipboardReceived { add { } remove { } }

    /// <inheritdoc />
    public event EventHandler<PhoneDevice>? PairRequested { add { } remove { } }

    /// <inheritdoc />
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var seed = new (string Id, string App, string Title, string Text, TimeSpan Ago, string? Reply, string[] Actions, IconSpec Icon)[]
        {
            ("0|com.google.android.apps.messaging|0", "Messages", "mBank", "Kod BLIK: 482913. Nie podawaj go nikomu.", TimeSpan.FromMinutes(1), null, ["Mark as read"], DemoIcons.PhoneMessages),
            ("0|com.google.android.apps.messaging|1", "Messages", "Arsh", "Coffee at 10? ☕", TimeSpan.FromMinutes(2), "reply-arsh-1", ["Mark as read"], DemoIcons.PhoneMessages),
            ("0|com.google.android.gm|2", "Gmail", "GitHub", "[NotchHub] Pull request #42 merged", TimeSpan.FromMinutes(14), null, ["Archive", "Reply"], DemoIcons.PhoneGmail),
            ("0|com.discord|3", "Discord", "Mainak (#general)", "anyone up for valorant tonight?", TimeSpan.FromMinutes(31), "reply-discord-3", [], DemoIcons.PhoneDiscord),
            ("0|com.google.android.youtube|4", "YouTube", "Fireship", "New video: Windows 11 widgets in 100 seconds", TimeSpan.FromHours(1.2), null, [], DemoIcons.PhoneYouTube),
            ("0|com.google.android.calendar|5", "Calendar", "Daily AI news research", "11:00 – 11:30", TimeSpan.FromHours(2), null, ["Snooze"], DemoIcons.PhoneCalendar),
        };
        var list = new List<PhoneNotification>();
        foreach (var s in seed)
        {
            var icon = await DemoAssets.IconAsync(s.Icon).ConfigureAwait(false);
            list.Add(new PhoneNotification(s.Id, s.App, s.Title, s.Text, $"{s.Title}: {s.Text}", now - s.Ago, true, false, s.Reply, s.Actions, icon));
        }
        lock (Gate) _notifications.AddRange(list);
        Raise(Changed);

        StartLiveLoop("phone notifications", () => TimeSpan.FromSeconds(45), PostNext, cancellationToken);
        StartLiveLoop("phone calls", () => TimeSpan.FromMinutes(3), SimulateCall, cancellationToken);
        StartLiveLoop("phone charging", () => TimeSpan.FromMinutes(2), ToggleCharging, cancellationToken);
    }

    private void ToggleCharging()
    {
        lock (Gate)
        {
            if (!ActiveLocked) return;
            var charging = !_battery.IsCharging;
            _battery = new PhoneBattery(_battery.Percent, charging, !charging && _battery.Percent <= 15);
        }
        Raise(Changed);
    }

    /// <summary>Rings for 8 s, then every other call is answered (talks 25 s) or missed (kept 1 min, like the real service).</summary>
    private void SimulateCall()
    {
        int gen;
        (string? Name, string Number) caller;
        lock (Gate)
        {
            if (!ActiveLocked || _call is { State: PhoneCallState.Ringing or PhoneCallState.Talking }) return;
            caller = Callers[_nextCaller % Callers.Length];
            var answered = _nextCaller++ % 2 == 0;
            gen = ++_callGen;
            _call = new PhoneCall(PhoneCallState.Ringing, caller.Name ?? caller.Number, caller.Number, DateTimeOffset.Now, null);
            After(TimeSpan.FromSeconds(8), "phone call answer", () => SetCall(gen, answered
                ? new PhoneCall(PhoneCallState.Talking, caller.Name ?? caller.Number, caller.Number, DateTimeOffset.Now, null)
                : new PhoneCall(PhoneCallState.Missed, caller.Name ?? caller.Number, caller.Number, DateTimeOffset.Now, null)));
            After(TimeSpan.FromSeconds(answered ? 33 : 68), "phone call end", () => SetCall(gen, null));
        }
        Raise(Changed);
    }

    private void SetCall(int gen, PhoneCall? call)
    {
        lock (Gate)
        {
            if (gen != _callGen) return;
            _call = call;
        }
        Raise(Changed);
    }

    private void PostNext()
    {
        (string App, string Title, string Text, IconSpec Icon, bool Reply) next;
        lock (Gate)
        {
            if (!ActiveLocked) return;
            next = Incoming[_nextIncoming++ % Incoming.Length];
        }
        var icon = DemoAssets.IconAsync(next.Icon).GetAwaiter().GetResult();
        var id = $"0|demo.{next.App.ToLowerInvariant()}|{Guid.NewGuid():N}";
        var n = new PhoneNotification(id, next.App, next.Title, next.Text, $"{next.Title}: {next.Text}", DateTimeOffset.Now,
            true, false, next.Reply ? "reply-" + id : null, [], icon);
        lock (Gate)
        {
            _notifications.Insert(0, n);
            if (_notifications.Count > 8) _notifications.RemoveAt(_notifications.Count - 1);
            _battery = _battery with { Percent = Math.Clamp(_battery.Percent + (_battery.IsCharging ? 1 : -1), 5, 100) };
        }
        Raise(Changed);
        Raise(NotificationPosted, new PhoneNotificationEventArgs(n));
    }

    /// <inheritdoc />
    public Task RefreshAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> RequestPairAsync(string deviceId) => Task.FromResult(SetPair(deviceId, PhonePairState.Paired));

    /// <inheritdoc />
    public Task AcceptPairAsync(string deviceId)
    {
        SetPair(deviceId, PhonePairState.Paired);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RejectPairAsync(string deviceId)
    {
        SetPair(deviceId, PhonePairState.NotPaired);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnpairAsync(string deviceId)
    {
        SetPair(deviceId, PhonePairState.NotPaired);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RingAsync()
    {
        Log.Info("DEMO: would ring the phone");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PingAsync(string? message = null)
    {
        Log.Info($"DEMO: would ping the phone ({message})");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendClipboardAsync(string text)
    {
        Log.Info($"DEMO: would send {text.Length} chars to the phone clipboard");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DismissNotificationAsync(string notificationId)
    {
        RemoveWhere(n => n.Id == notificationId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReplyToNotificationAsync(string replyId, string message)
    {
        Log.Info($"DEMO: reply '{message}' via {replyId}");
        RemoveWhere(n => n.ReplyId == replyId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvokeNotificationActionAsync(string notificationId, string action)
    {
        Log.Info($"DEMO: action '{action}' on {notificationId}");
        RemoveWhere(n => n.Id == notificationId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RequestNotificationsAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task MuteRingerAsync()
    {
        Log.Info("DEMO: would mute the ringing phone");
        return Task.CompletedTask;
    }

    private void RemoveWhere(Predicate<PhoneNotification> match)
    {
        bool removed;
        lock (Gate) removed = _notifications.RemoveAll(match) > 0;
        if (removed) Raise(Changed);
    }

    private bool SetPair(string deviceId, PhonePairState state)
    {
        lock (Gate)
        {
            if (deviceId != PhoneId) return false;
            _device = _device with { PairState = state };
        }
        Raise(Changed);
        return true;
    }
}
