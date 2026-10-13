using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using NotchHub.KdeConnect.Protocol;

namespace NotchHub.KdeConnect.Tests;

/// <summary>A second KDE Connect endpoint on 127.0.0.1 that behaves like the Android app (reuses the protocol classes).</summary>
internal sealed class FakePhone : IDisposable
{
    private readonly List<NetworkPacket> _received = [];
    private readonly TcpListener _tcp;
    private readonly UdpClient _udp;

    public FakePhone()
    {
        var id = "phone_" + Guid.NewGuid().ToString("N")[..26];
        Local = new LocalInfo(id, "Test Phone", CertificateHelper.Create(id), "phone");
        TcpListener? l = null;
        for (var port = 1716; port <= 1764 && l is null; port++)
        {
            try { l = new TcpListener(IPAddress.Loopback, port); l.Start(); }
            catch (SocketException) { l = null; }
        }
        _tcp = l ?? throw new InvalidOperationException("no free port");
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    }

    public LocalInfo Local { get; }
    public string Id => Local.DeviceId;
    public int TcpPort => ((IPEndPoint)_tcp.LocalEndpoint).Port;
    public IPEndPoint UdpEndpoint => (IPEndPoint)_udp.Client.LocalEndPoint!;
    public DeviceLink? Link { get; private set; }

    /// <summary>Waits for the PC's UDP announcement and connects to it (phone = TLS server).</summary>
    public async Task ConnectFromAnnouncementAsync(CancellationToken ct)
    {
        while (true)
        {
            var r = await _udp.ReceiveAsync(ct);
            var p = NetworkPacket.TryParse(Encoding.UTF8.GetString(r.Buffer));
            if (!DeviceIdentity.IsValidDiscovery(p)) continue;
            var link = await LinkHandshake.ConnectAsync(new IPEndPoint(IPAddress.Loopback, p!.GetInt("tcpPort")!.Value),
                p.GetString("deviceId")!, 8, Local, _ => null, ct);
            Attach(link);
            return;
        }
    }

    /// <summary>Sends our UDP identity to the PC and accepts its TCP connection (phone = TLS client).</summary>
    public async Task AnnounceAndAcceptAsync(int pcUdpPort, CancellationToken ct)
    {
        var bytes = DeviceIdentity.Build(Id, Local.DeviceName, tcpPort: TcpPort, deviceType: "phone").SerializeUtf8();
        await _udp.SendAsync(bytes, new IPEndPoint(IPAddress.Loopback, pcUdpPort), ct);
        var client = await _tcp.AcceptTcpClientAsync(ct);
        Attach(await LinkHandshake.AcceptAsync(client, Local, _ => null, ct));
    }

    private void Attach(DeviceLink link)
    {
        Link = link;
        link.PacketReceived += (_, p) => { lock (_received) _received.Add(p); };
        link.Start();
    }

    public Task<bool> SendAsync(string type, JsonObject body, long payloadSize = 0, int? payloadPort = null) =>
        Link!.SendAsync(new NetworkPacket(type, body) { PayloadSize = payloadSize, PayloadPort = payloadPort });

    public async Task<NetworkPacket> WaitForAsync(string type, Func<NetworkPacket, bool>? match = null, int timeoutMs = 10000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            lock (_received)
            {
                var p = _received.FirstOrDefault(x => x.Type == type && (match?.Invoke(x) ?? true));
                if (p is not null) { _received.Remove(p); return p; }
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"phone never received {type}");
    }

    /// <summary>Offers a payload like the Android app: listen on a port, we are the TLS server.</summary>
    public int ServePayload(byte[] data)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                using var c = await l.AcceptTcpClientAsync();
                await using var ssl = new SslStream(c.GetStream(), false);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = Local.Certificate,
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null,
                    EnabledSslProtocols = LinkHandshake.Protocols,
                });
                await ssl.WriteAsync(data);
                await ssl.FlushAsync();
                await Task.Delay(300);
            }
            catch { }
            finally { l.Stop(); }
        });
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        Link?.Close();
        _tcp.Stop();
        _udp.Dispose();
        Local.Certificate.Dispose();
    }
}
