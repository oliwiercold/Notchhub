using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NotchHub.Core;

namespace NotchHub.KdeConnect.Protocol;

/// <summary>Local identity used for the TLS handshake.</summary>
internal sealed record LocalInfo(string DeviceId, string DeviceName, X509Certificate2 Certificate, string DeviceType = "desktop");

/// <summary>An established, encrypted link to a remote device (one JSON packet per line over TLS).</summary>
internal sealed class DeviceLink : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly SslStream _ssl;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private int _closed;

    public DeviceLink(TcpClient tcp, SslStream ssl, NetworkPacket identity, X509Certificate2 peerCertificate, IPAddress address)
    {
        _tcp = tcp;
        _ssl = ssl;
        Identity = identity;
        DeviceId = identity.GetString("deviceId")!;
        Name = DeviceIdentity.FilterName(identity.GetString("deviceName"));
        DeviceType = DeviceIdentity.NormalizeType(identity.GetString("deviceType"));
        ProtocolVersion = identity.GetInt("protocolVersion") ?? 0;
        PeerCertificate = peerCertificate;
        Address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    public NetworkPacket Identity { get; }
    public string DeviceId { get; }
    public string Name { get; }
    public string DeviceType { get; }
    public int ProtocolVersion { get; }
    public X509Certificate2 PeerCertificate { get; }
    public IPAddress Address { get; }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public event Action<DeviceLink, NetworkPacket>? PacketReceived;
    public event Action<DeviceLink>? Closed;

    public void Start() => Task.Run(ReadLoopAsync);

    public async Task<bool> SendAsync(NetworkPacket packet)
    {
        if (IsClosed) return false;
        var bytes = packet.SerializeUtf8();
        try
        {
            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await _ssl.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
                await _ssl.FlushAsync(timeout.Token).ConfigureAwait(false);
                return true;
            }
            finally { _sendLock.Release(); }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException or InvalidOperationException)
        {
            Log.Debug($"KdeConnect: send to {DeviceId} failed: {ex.Message}");
            Close();
            return false;
        }
    }

    /// <summary>Largest packet line accepted from a device that is not paired (pair / identity packets are tiny).</summary>
    public const int UnpairedMaxLineBytes = 64 * 1024;
    /// <summary>Largest packet line accepted from a paired device (payloads travel on separate sockets).</summary>
    public const int PairedMaxLineBytes = 4 * 1024 * 1024;

    /// <summary>Line-length cap, evaluated for every line (so it grows once the device gets paired). Set before <see cref="Start"/>.</summary>
    public Func<int> MaxLineBytes { get; set; } = static () => UnpairedMaxLineBytes;

    private async Task ReadLoopAsync()
    {
        try
        {
            await ReadLinesAsync(_ssl, MaxLineBytes, line =>
            {
                if (_cts.IsCancellationRequested) return;
                var packet = NetworkPacket.TryParse(line);
                if (packet is null) { Log.Debug("KdeConnect: unparsable packet ignored"); return; }
                try { PacketReceived?.Invoke(this, packet); }
                catch (Exception ex) { Log.Error($"KdeConnect: handling {packet.Type} failed", ex); }
            }, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException or AuthenticationException or InvalidOperationException or InvalidDataException)
        {
            Log.Debug($"KdeConnect: link {DeviceId} read ended: {ex.Message}");
        }
        catch (Exception ex) { Log.Error($"KdeConnect: link {DeviceId} read loop", ex); }
        finally { Close(); }
    }

    /// <summary>
    /// Reads LF-terminated UTF-8 lines and hands each non-empty one to <paramref name="onLine"/>. Throws
    /// <see cref="InvalidDataException"/> as soon as a line grows past <paramref name="maxLineBytes"/>, so a peer that never
    /// sends a newline cannot make us buffer without limit. Returns at end of stream.
    /// </summary>
    internal static async Task ReadLinesAsync(Stream stream, Func<int> maxLineBytes, Action<string> onLine, CancellationToken ct)
    {
        var chunk = new byte[16384];
        var acc = new byte[4096];
        var len = 0;
        while (true)
        {
            var n = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (n == 0) return;
            var start = 0;
            while (start < n)
            {
                var nl = Array.IndexOf(chunk, (byte)'\n', start, n - start);
                var count = (nl < 0 ? n : nl) - start;
                if (count > 0)
                {
                    var max = maxLineBytes();
                    if ((long)len + count > max) throw new InvalidDataException($"packet line larger than {max} bytes");
                    if (len + count > acc.Length) Array.Resize(ref acc, Math.Min(max, Math.Max(acc.Length * 2, len + count)));
                    Buffer.BlockCopy(chunk, start, acc, len, count);
                    len += count;
                }
                if (nl < 0) break;
                var lineLen = len > 0 && acc[len - 1] == (byte)'\r' ? len - 1 : len;
                if (lineLen > 0) onLine(Encoding.UTF8.GetString(acc, 0, lineLen));
                len = 0;
                if (acc.Length > 64 * 1024) acc = new byte[4096];
                start = nl + 1;
            }
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try { _cts.Cancel(); } catch { }
        try { _ssl.Dispose(); } catch { }
        try { _tcp.Dispose(); } catch { }
        try { Closed?.Invoke(this); } catch (Exception ex) { Log.Error("KdeConnect: link closed handler", ex); }
    }

    public void Dispose() => Close();

    /// <summary>Downloads a payload the peer offers on <paramref name="port"/> (we open TCP and act as the TLS client).</summary>
    public async Task<byte[]?> DownloadPayloadAsync(int port, long size, LocalInfo local, long maxSize, CancellationToken ct)
    {
        if (size <= 0 || size > maxSize || IsClosed) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var tcp = new TcpClient(Address.AddressFamily);
        await tcp.ConnectAsync(new IPEndPoint(Address, port), timeout.Token).ConfigureAwait(false);
        await using var ssl = new SslStream(tcp.GetStream(), false);
        var peerDer = PeerCertificate.RawData;
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = DeviceId,
            ClientCertificates = new X509CertificateCollection { local.Certificate },
            LocalCertificateSelectionCallback = (_, _, _, _, _) => local.Certificate,
            RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && cert.GetRawCertData().AsSpan().SequenceEqual(peerDer),
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            EnabledSslProtocols = LinkHandshake.Protocols,
        }, timeout.Token).ConfigureAwait(false);
        var buffer = new byte[size];
        await ssl.ReadExactlyAsync(buffer, timeout.Token).ConfigureAwait(false);
        return buffer;
    }
}

/// <summary>
/// Connection setup (kdeconnect-kde core/backends/lan/lanlinkprovider.cpp): the side that OPENED the TCP connection sends its
/// identity in plain text and then becomes the TLS SERVER; the accepting side reads that identity and is the TLS CLIENT.
/// After the handshake both sides send their full identity again over TLS (protocol v8).
/// </summary>
internal static class LinkHandshake
{
    public const SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>We discovered <paramref name="expectedDeviceId"/> (UDP) and connect to its TCP port.</summary>
    public static async Task<DeviceLink> ConnectAsync(IPEndPoint endpoint, string expectedDeviceId, int expectedProtocolVersion,
        LocalInfo local, Func<string, byte[]?> pinnedCertificate, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        var tcp = new TcpClient(endpoint.AddressFamily);
        SslStream? ssl = null;
        try
        {
            ConfigureSocket(tcp.Client);
            await tcp.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();
            var hello = DeviceIdentity.Build(local.DeviceId, local.DeviceName, targetDeviceId: expectedDeviceId,
                targetProtocolVersion: expectedProtocolVersion, deviceType: local.DeviceType);
            await stream.WriteAsync(hello.SerializeUtf8(), timeout.Token).ConfigureAwait(false);
            ssl = new SslStream(stream, false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = local.Certificate,
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = Validator(expectedDeviceId, pinnedCertificate),
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                EnabledSslProtocols = Protocols,
            }, timeout.Token).ConfigureAwait(false);
            return await FinishAsync(tcp, ssl, expectedDeviceId, expectedProtocolVersion, local, endpoint.Address, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            ssl?.Dispose();
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>A remote device connected to our TCP listener.</summary>
    public static async Task<DeviceLink> AcceptAsync(TcpClient tcp, LocalInfo local, Func<string, byte[]?> pinnedCertificate, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        SslStream? ssl = null;
        try
        {
            ConfigureSocket(tcp.Client);
            var stream = tcp.GetStream();
            var hello = NetworkPacket.TryParse(await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false));
            if (!DeviceIdentity.IsValidDiscovery(hello)) throw new InvalidDataException("invalid identity packet");
            var deviceId = hello!.GetString("deviceId")!;
            var target = hello.GetString("targetDeviceId");
            if (!string.IsNullOrEmpty(target) && target != local.DeviceId) throw new InvalidDataException("connection packet for another device");
            var version = hello.GetInt("protocolVersion") ?? 0;
            if (version < DeviceIdentity.ProtocolVersion) throw new InvalidDataException($"unsupported protocol version {version}");
            ssl = new SslStream(stream, false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = deviceId,
                ClientCertificates = new X509CertificateCollection { local.Certificate },
                LocalCertificateSelectionCallback = (_, _, _, _, _) => local.Certificate,
                RemoteCertificateValidationCallback = Validator(deviceId, pinnedCertificate),
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                EnabledSslProtocols = Protocols,
            }, timeout.Token).ConfigureAwait(false);
            var address = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
            return await FinishAsync(tcp, ssl, deviceId, version, local, address, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            ssl?.Dispose();
            tcp.Dispose();
            throw;
        }
    }

    private static async Task<DeviceLink> FinishAsync(TcpClient tcp, SslStream ssl, string deviceId, int version, LocalInfo local,
        IPAddress address, CancellationToken ct)
    {
        var mine = DeviceIdentity.Build(local.DeviceId, local.DeviceName, deviceType: local.DeviceType);
        await ssl.WriteAsync(mine.SerializeUtf8(), ct).ConfigureAwait(false);
        await ssl.FlushAsync(ct).ConfigureAwait(false);
        var identity = NetworkPacket.TryParse(await ReadLineAsync(ssl, ct).ConfigureAwait(false));
        if (!DeviceIdentity.IsValidIdentity(identity)) throw new InvalidDataException("invalid encrypted identity");
        if (identity!.GetString("deviceId") != deviceId) throw new InvalidDataException("device id changed during handshake");
        if ((identity.GetInt("protocolVersion") ?? 0) != version) throw new InvalidDataException("protocol version changed during handshake");
        if (ssl.RemoteCertificate is null) throw new AuthenticationException("peer sent no certificate");
        var cert = CertificateHelper.FromRemote(ssl.RemoteCertificate);
        if (CertificateHelper.CommonName(cert) != deviceId) throw new AuthenticationException("certificate CN does not match device id");
        return new DeviceLink(tcp, ssl, identity, cert, address);
    }

    private static RemoteCertificateValidationCallback Validator(string deviceId, Func<string, byte[]?> pinned) =>
        (_, cert, _, _) =>
        {
            if (cert is null) return false;
            var der = pinned(deviceId);
            return der is null || cert.GetRawCertData().AsSpan().SequenceEqual(der);
        };

    /// <summary>Reads one LF-terminated line byte by byte (never over-reads, so TLS can start right after).</summary>
    public static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct, int max = DeviceIdentity.MaxIdentityPacketSize)
    {
        var buffer = new byte[max];
        var one = new byte[1];
        var count = 0;
        while (true)
        {
            var n = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException();
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(buffer, 0, count);
            if (count >= max) throw new InvalidDataException("identity packet too large");
            buffer[count++] = one[0];
        }
    }

    public static void ConfigureSocket(Socket socket)
    {
        try
        {
            socket.NoDelay = true;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (Exception ex) { Log.Debug($"KdeConnect: keepalive options not set: {ex.Message}"); }
    }
}
