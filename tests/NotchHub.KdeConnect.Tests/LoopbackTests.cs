using System.Net;
using System.Text.Json.Nodes;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.KdeConnect.Protocol;

namespace NotchHub.KdeConnect.Tests;

/// <summary>PhoneService against an in-process fake phone, everything bound to 127.0.0.1.</summary>
[Collection("loopback")]
public sealed class LoopbackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nh-kde-tests", Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(60));

    public LoopbackTests()
    {
        Directory.CreateDirectory(_dir);
        AppPaths.Initialize(Path.Combine(Path.GetTempPath(), "nh-kde-tests", "root"));
    }

    public void Dispose()
    {
        _cts.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private PhoneService CreateService(FakePhone phone, out SettingsService settings)
    {
        settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        settings.Current.Phone.DeviceName = "Test PC";
        return new PhoneService(settings, new KdeConnectOptions
        {
            BindAddress = IPAddress.Loopback,
            UdpPort = 0,
            DataDirectory = Path.Combine(_dir, "kdeconnect"),
            AnnounceTargets = [phone.UdpEndpoint],
            AcceptLoopback = true,
            AnnounceInterval = TimeSpan.FromSeconds(2),
            OfflineGrace = TimeSpan.FromMilliseconds(400),
            MissedCallLinger = TimeSpan.FromMilliseconds(600),
        });
    }

    private static async Task Until(Func<bool> condition, string what, int timeoutMs = 10000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > until) throw new TimeoutException(what);
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Full_session_pairing_battery_notifications_clipboard()
    {
        using var phone = new FakePhone();
        using var svc = CreateService(phone, out _);
        PhoneDevice? pairRequest = null;
        PhoneNotification? posted = null;
        string? clip = null;
        svc.PairRequested += (_, d) => pairRequest = d;
        svc.NotificationPosted += (_, e) => posted = e.Notification;
        svc.ClipboardReceived += (_, t) => clip = t;

        await svc.StartAsync();
        Assert.True(svc.IsRunning);
        Assert.Matches("^[0-9a-f]{32}$", svc.LocalDeviceId);
        Assert.Equal("Test PC", svc.LocalDeviceName);

        // Discovery: the phone hears our UDP announcement and opens TCP (phone = TLS server, PC = TLS client).
        await phone.ConnectFromAnnouncementAsync(_cts.Token);
        Assert.Equal(svc.LocalDeviceId, phone.Link!.DeviceId);
        Assert.Equal("desktop", phone.Link.DeviceType);
        await Until(() => svc.Devices.Any(d => d.Id == phone.Id && d.IsReachable), "device visible");
        Assert.Equal(PhonePairState.NotPaired, svc.Devices.Single().PairState);
        Assert.Equal("phone", svc.Devices.Single().DeviceType);
        Assert.Null(svc.ActiveDevice);

        // Pairing requested by the phone, accepted on the PC.
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await phone.SendAsync(NetworkPacket.Pair, new JsonObject { ["pair"] = true, ["timestamp"] = ts });
        await Until(() => pairRequest is not null, "PairRequested");
        Assert.Equal(PhonePairState.RequestedByPeer, pairRequest!.PairState);
        Assert.Equal(CertificateHelper.VerificationKey(phone.Local.Certificate, phone.Link.PeerCertificate, ts), pairRequest.VerificationKey);
        await svc.AcceptPairAsync(phone.Id);
        var accept = await phone.WaitForAsync(NetworkPacket.Pair);
        Assert.True(accept.GetBool("pair"));
        Assert.Equal(PhonePairState.Paired, svc.ActiveDevice?.PairState);
        await phone.WaitForAsync(NetworkPacket.BatteryRequest);
        await phone.WaitForAsync(NetworkPacket.NotificationRequest, p => p.GetBool("request") == true);
        Assert.Contains(phone.Id, File.ReadAllText(Path.Combine(_dir, "kdeconnect", "devices.json")));

        // Battery + signal.
        await phone.SendAsync(NetworkPacket.Battery, new JsonObject { ["currentCharge"] = 57, ["isCharging"] = true, ["thresholdEvent"] = 0 });
        await Until(() => svc.Battery is not null, "battery");
        Assert.Equal(new PhoneBattery(57, true, false), svc.Battery);
        await phone.SendAsync(NetworkPacket.Connectivity, new JsonObject
        {
            ["signalStrengths"] = new JsonObject { ["6"] = new JsonObject { ["networkType"] = "LTE", ["signalStrength"] = 3 } },
        });
        await Until(() => svc.Signal is not null, "signal");
        Assert.Equal(new PhoneSignal("LTE", 3), svc.Signal);

        // Notification with an icon payload.
        var icon = new byte[5000];
        new Random(1).NextBytes(icon);
        var port = phone.ServePayload(icon);
        await phone.SendAsync(NetworkPacket.Notification, new JsonObject
        {
            ["id"] = "0|com.whatsapp|1|null|10123", ["appName"] = "WhatsApp", ["title"] = "Ala", ["text"] = "Cześć!",
            ["ticker"] = "Ala: Cześć!", ["isClearable"] = true, ["time"] = "1700000000000", ["silent"] = "false",
            ["requestReplyId"] = "r-1", ["actions"] = new JsonArray("Mark as read"),
        }, icon.Length, port);
        await Until(() => posted is not null, "NotificationPosted");
        Assert.Equal("WhatsApp", posted!.AppName);
        Assert.Equal("r-1", posted.ReplyId);
        Assert.Equal(["Mark as read"], posted.Actions);
        Assert.Equal(icon, posted.IconPng);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), posted.Time);
        Assert.Single(svc.Notifications);
        Assert.Equal(icon, svc.Notifications[0].IconPng);

        // Reply + action + dismiss reach the phone.
        await svc.ReplyToNotificationAsync("r-1", "Hej");
        var reply = await phone.WaitForAsync(NetworkPacket.NotificationReply);
        Assert.Equal("Hej", reply.GetString("message"));
        await svc.InvokeNotificationActionAsync("0|com.whatsapp|1|null|10123", "Mark as read");
        var action = await phone.WaitForAsync(NetworkPacket.NotificationAction);
        Assert.Equal("Mark as read", action.GetString("action"));
        await svc.DismissNotificationAsync("0|com.whatsapp|1|null|10123");
        var cancel = await phone.WaitForAsync(NetworkPacket.NotificationRequest, p => p.Has("cancel"));
        Assert.Equal("0|com.whatsapp|1|null|10123", cancel.GetString("cancel"));
        Assert.Empty(svc.Notifications);

        // Clipboard both ways, ring and ping.
        await phone.SendAsync(NetworkPacket.Clipboard, new JsonObject { ["content"] = "z telefonu" });
        await Until(() => clip is not null, "ClipboardReceived");
        Assert.Equal("z telefonu", clip);
        await svc.SendClipboardAsync("z komputera");
        Assert.Equal("z komputera", (await phone.WaitForAsync(NetworkPacket.Clipboard)).GetString("content"));
        await svc.RingAsync();
        await phone.WaitForAsync(NetworkPacket.FindMyPhone);
        await svc.PingAsync("hi");
        Assert.Equal("hi", (await phone.WaitForAsync(NetworkPacket.Ping)).GetString("message"));

        // Disconnect keeps the paired device (offline); live data survives a short hand-over, then is cleared.
        phone.Link.Close();
        await Until(() => svc.ActiveDevice is { IsReachable: false }, "offline");
        Assert.Equal(57, svc.Battery?.Percent);
        await Until(() => svc.Battery is null, "battery cleared after grace");
        Assert.Empty(svc.Notifications);
    }

    [Fact]
    public async Task Telephony_ringing_talking_missed_and_mute()
    {
        using var phone = new FakePhone();
        using var svc = CreateService(phone, out _);
        await svc.StartAsync();
        await phone.ConnectFromAnnouncementAsync(_cts.Token);
        await Until(() => svc.Devices.Any(d => d.Id == phone.Id && d.IsReachable), "device visible");
        await phone.SendAsync(NetworkPacket.Pair, new JsonObject { ["pair"] = true, ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        await Until(() => svc.Devices.Single().PairState == PhonePairState.RequestedByPeer, "pair requested");
        await svc.AcceptPairAsync(phone.Id);
        await Until(() => svc.ActiveDevice?.PairState == PhonePairState.Paired, "paired");

        // Ringing with a thumbnail.
        byte[] thumb = [1, 2, 3, 4];
        await phone.SendAsync(NetworkPacket.Telephony, new JsonObject
        {
            ["event"] = "ringing", ["contactName"] = "Mama", ["phoneNumber"] = "+48 600 100 200", ["phoneThumbnail"] = Convert.ToBase64String(thumb),
        });
        await Until(() => svc.ActiveCall is not null, "ringing");
        Assert.Equal(PhoneCallState.Ringing, svc.ActiveCall!.State);
        Assert.Equal("Mama", svc.ActiveCall.ContactName);
        Assert.Equal("+48 600 100 200", svc.ActiveCall.PhoneNumber);
        Assert.Equal(thumb, svc.ActiveCall.ContactPhoto);

        await svc.MuteRingerAsync();
        Assert.Equal("mute", (await phone.WaitForAsync(NetworkPacket.TelephonyRequestMute)).GetString("action"));

        // Answered, then hung up (cancel of the talking packet).
        await phone.SendAsync(NetworkPacket.Telephony, new JsonObject { ["event"] = "talking", ["contactName"] = "Mama", ["phoneNumber"] = "+48 600 100 200" });
        await Until(() => svc.ActiveCall?.State == PhoneCallState.Talking, "talking");
        Assert.Equal(thumb, svc.ActiveCall!.ContactPhoto);
        await phone.SendAsync(NetworkPacket.Telephony, new JsonObject { ["event"] = "talking", ["phoneNumber"] = "+48 600 100 200", ["isCancel"] = true });
        await Until(() => svc.ActiveCall is null, "ended");

        // Unanswered: missedCall, then the ringing cancel must not wipe it; it expires on its own.
        await phone.SendAsync(NetworkPacket.Telephony, new JsonObject { ["event"] = "ringing", ["phoneNumber"] = "123456789" });
        await Until(() => svc.ActiveCall?.State == PhoneCallState.Ringing, "ringing 2");
        Assert.Equal("123456789", svc.ActiveCall!.ContactName);
        await phone.SendAsync(NetworkPacket.Telephony, new JsonObject { ["event"] = "missedCall", ["phoneNumber"] = "123456789" });
        await phone.SendAsync(NetworkPacket.Telephony, new JsonObject { ["event"] = "ringing", ["phoneNumber"] = "123456789", ["isCancel"] = true });
        await Until(() => svc.ActiveCall?.State == PhoneCallState.Missed, "missed");
        await Task.Delay(150);
        Assert.Equal(PhoneCallState.Missed, svc.ActiveCall?.State);
        await Until(() => svc.ActiveCall is null, "missed call expired");

        // Legacy sms packets are ignored.
        await phone.SendAsync(NetworkPacket.Telephony, new JsonObject { ["event"] = "sms", ["messageBody"] = "x" });
        await svc.PingAsync();
        await phone.WaitForAsync(NetworkPacket.Ping);
        Assert.Null(svc.ActiveCall);
    }

    [Fact]
    public async Task Phone_initiated_link_pair_request_by_pc_reject_and_unpair()
    {
        using var phone = new FakePhone();
        using var svc = CreateService(phone, out var settings);
        await svc.StartAsync();

        // Phone announces itself; the PC connects (PC = TLS server, phone = TLS client).
        await phone.AnnounceAndAcceptAsync(svc.UdpPort, _cts.Token);
        await Until(() => svc.Devices.Any(d => d.Id == phone.Id && d.IsReachable), "device visible");

        // Non-pair packets from an unpaired device are refused.
        await phone.SendAsync(NetworkPacket.Battery, new JsonObject { ["currentCharge"] = 10 });
        Assert.False((await phone.WaitForAsync(NetworkPacket.Pair)).GetBool("pair"));
        Assert.Null(svc.Battery);

        // PC requests pairing, phone accepts.
        Assert.True(await svc.RequestPairAsync(phone.Id));
        var req = await phone.WaitForAsync(NetworkPacket.Pair);
        Assert.True(req.GetBool("pair"));
        Assert.NotNull(req.GetLong("timestamp"));
        Assert.Equal(PhonePairState.RequestedByUs, svc.Devices.Single().PairState);
        Assert.Equal(CertificateHelper.VerificationKey(phone.Local.Certificate, phone.Link!.PeerCertificate, req.GetLong("timestamp")!.Value),
            svc.Devices.Single().VerificationKey);
        await phone.SendAsync(NetworkPacket.Pair, new JsonObject { ["pair"] = true });
        await Until(() => svc.ActiveDevice?.PairState == PhonePairState.Paired, "paired");

        // Phone unpairs.
        await phone.SendAsync(NetworkPacket.Pair, new JsonObject { ["pair"] = false });
        await Until(() => svc.ActiveDevice is null, "unpaired");
        Assert.DoesNotContain(phone.Id, File.ReadAllText(Path.Combine(_dir, "kdeconnect", "devices.json")));

        // Pair request with a skewed clock is rejected.
        await phone.SendAsync(NetworkPacket.Pair, new JsonObject { ["pair"] = true, ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7200 });
        Assert.False((await phone.WaitForAsync(NetworkPacket.Pair)).GetBool("pair"));

        // Disabling the endpoint via settings stops it live.
        settings.Update(s => s.Phone.Enabled = false);
        await Until(() => !svc.IsRunning, "stopped");
        Assert.Empty(svc.Devices);
    }

    [Fact]
    public async Task Identity_persists_across_restarts()
    {
        using var phone = new FakePhone();
        string id;
        using (var a = CreateService(phone, out _)) { await a.StartAsync(); id = a.LocalDeviceId; }
        using var b = CreateService(phone, out _);
        await b.StartAsync();
        Assert.Equal(id, b.LocalDeviceId);
    }
}
