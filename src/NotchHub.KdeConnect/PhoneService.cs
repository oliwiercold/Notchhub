using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.KdeConnect.Protocol;

namespace NotchHub.KdeConnect;

/// <summary>
/// Built-in KDE Connect endpoint: this PC announces itself as a "desktop" device, phones running the KDE Connect app
/// pair with it and then stream battery, signal, notifications and clipboard. See PROTOCOL.md.
/// </summary>
public sealed class PhoneService : IPhoneService
{
    private const long MaxIconBytes = 2 * 1024 * 1024;
    private const long AllowedPairSkewSeconds = 1800;
    private const int MaxNotifications = 100;
    private const int MaxUnpairedDevices = 42;

    private readonly ISettingsService _settings;
    private readonly KdeConnectOptions _options;
    private readonly object _gate = new();
    private readonly object _lifecycle = new();
    private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _connecting = new(StringComparer.Ordinal);

    private LocalInfo? _local;
    private string _localId = "";
    private string _localName;
    private CancellationTokenSource? _runCts;
    private Socket? _udp;
    private TcpListener? _listener;
    private int _tcpPort;
    private int _udpPort;
    private Timer? _announceTimer;
    private Timer? _networkDebounce;
    private bool _started;
    private bool _disposed;
    private volatile bool _running;

    private volatile PhoneDevice[] _snapDevices = [];
    private volatile PhoneDevice? _snapActive;
    private volatile PhoneBattery? _snapBattery;
    private volatile PhoneSignal? _snapSignal;
    private volatile PhoneNotification[] _snapNotifications = [];
    private volatile PhoneCall? _snapCall;

    public PhoneService(ISettingsService settings) : this(settings, new KdeConnectOptions()) { }

    internal PhoneService(ISettingsService settings, KdeConnectOptions options)
    {
        _settings = settings;
        _options = options;
        _localName = ComputeName();
    }

    // ---------------------------------------------------------------- state

    public bool IsRunning => _running;
    public string LocalDeviceId => _localId;
    public string LocalDeviceName => _localName;
    public IReadOnlyList<PhoneDevice> Devices => _snapDevices;
    public PhoneDevice? ActiveDevice => _snapActive;
    public PhoneBattery? Battery => _snapBattery;
    public PhoneSignal? Signal => _snapSignal;
    public IReadOnlyList<PhoneNotification> Notifications => _snapNotifications;
    public PhoneCall? ActiveCall => _snapCall;

    internal int TcpPort => _tcpPort;
    internal int UdpPort => _udpPort;

    public event EventHandler? Changed;
    public event EventHandler<PhoneNotificationEventArgs>? NotificationPosted;
    public event EventHandler<string>? ClipboardReceived;
    public event EventHandler<PhoneDevice>? PairRequested;

    private sealed class DeviceState(string id)
    {
        public string Id { get; } = id;
        public string Name = "";
        public string Type = "phone";
        public int ProtocolVersion;
        public PhonePairState Pair;
        public byte[]? PairedCert;
        public string? Address;
        public DeviceLink? Link;
        public long PairTimestamp;
        public string? VerificationKey;
        public CancellationTokenSource? PairTimer;
        public PhoneBattery? Battery;
        public PhoneSignal? Signal;
        public List<PhoneNotification> Notifications = [];
        public PhoneCall? Call;
        /// <summary>Bumped on every call change; a missed-call expiry only clears the call it was scheduled for.</summary>
        public int CallGen;
        /// <summary>Bumped on every link attach / close; the offline-grace timer only clears data of the drop it belongs to.</summary>
        public int LinkGen;
        public bool IsReachable => Link is { IsClosed: false };
    }

    internal sealed class PairedRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "phone";
        public byte[] Certificate { get; set; } = [];
        public string? LastAddress { get; set; }
        public int ProtocolVersion { get; set; }
    }

    internal sealed class IdentityRecord
    {
        public string DeviceId { get; set; } = "";
    }

    // ---------------------------------------------------------------- lifecycle

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycle)
        {
            if (_started || _disposed) return Task.CompletedTask;
            _started = true;
        }
        _settings.Changed += OnSettingsChanged;
        try { NetworkChange.NetworkAddressChanged += OnNetworkChanged; }
        catch (Exception ex) { Log.Warn("KdeConnect: network change notifications unavailable", ex); }
        return Task.Run(ApplySettings, cancellationToken);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Task.Run(ApplySettings).Forget("KdeConnect.ApplySettings");

    private void ApplySettings()
    {
        try
        {
            lock (_lifecycle)
            {
                if (_disposed) return;
                var name = ComputeName();
                var nameChanged = name != _localName;
                if (nameChanged)
                {
                    _localName = name;
                    if (_local is not null) _local = _local with { DeviceName = name };
                }
                var enabled = _settings.Current.Phone?.Enabled ?? true;
                if (enabled && !_running) StartEndpoint();
                else if (!enabled && _running) StopEndpoint();
                else if (nameChanged && _running) Announce();
                if (nameChanged) Publish();
            }
        }
        catch (Exception ex) { Log.Error("KdeConnect: applying settings failed", ex); }
    }

    private string ComputeName()
    {
        var name = DeviceIdentity.FilterName(_settings.Current.Phone?.DeviceName);
        if (name.Length == 0) name = DeviceIdentity.FilterName(Environment.MachineName);
        return name.Length == 0 ? "NotchHub" : name;
    }

    private string DataDir => _options.DataDirectory is { } d ? Directory.CreateDirectory(d).FullName : AppPaths.Dir("kdeconnect");

    private void StartEndpoint()
    {
        try { EnsureIdentity(); }
        catch (Exception ex) { Log.Error("KdeConnect: cannot create device identity", ex); return; }

        TcpListener? listener = null;
        for (var port = _options.TcpPortMin; port <= _options.TcpPortMax && listener is null; port++)
        {
            try
            {
                var l = new TcpListener(_options.BindAddress, port);
                l.Server.ExclusiveAddressUse = true;
                l.Start();
                listener = l;
            }
            catch (SocketException) { }
        }
        if (listener is null)
        {
            Log.Error($"KdeConnect: no free TCP port in {_options.TcpPortMin}-{_options.TcpPortMax}");
            return;
        }
        _listener = listener;
        _tcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.EnableBroadcast = true;
            if (OperatingSystem.IsWindows())
            {
                try { udp.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, [0, 0, 0, 0], null); } catch { }
            }
            udp.Bind(new IPEndPoint(_options.BindAddress, _options.UdpPort));
            _udp = udp;
            _udpPort = ((IPEndPoint)udp.LocalEndPoint!).Port;
        }
        catch (Exception ex)
        {
            Log.Warn($"KdeConnect: UDP {_options.UdpPort} unavailable, discovery works only via our broadcasts: {ex.Message}");
            _udp = null;
        }

        var cts = new CancellationTokenSource();
        _runCts = cts;
        _running = true;
        Task.Run(() => AcceptLoopAsync(listener, cts.Token));
        if (_udp is { } u) Task.Run(() => UdpLoopAsync(u, cts.Token));
        _announceTimer = new Timer(_ => PeriodicAnnounce(), null, TimeSpan.FromMilliseconds(200), _options.AnnounceInterval);
        Log.Info($"KdeConnect: endpoint running (tcp {_tcpPort}, udp {(_udp is null ? "off" : _udpPort.ToString())})");
        Publish();
    }

    private void StopEndpoint()
    {
        _running = false;
        try { _runCts?.Cancel(); } catch { }
        _runCts?.Dispose();
        _runCts = null;
        _announceTimer?.Dispose();
        _announceTimer = null;
        try { _listener?.Stop(); } catch { }
        _listener = null;
        try { _udp?.Dispose(); } catch { }
        _udp = null;
        _tcpPort = 0;
        _udpPort = 0;

        List<DeviceLink> links;
        lock (_gate)
        {
            links = _devices.Values.Where(d => d.Link is not null).Select(d => d.Link!).ToList();
            foreach (var d in _devices.Values.ToList())
            {
                d.Link = null;
                CancelPairTimer(d);
                if (d.Pair != PhonePairState.Paired) _devices.Remove(d.Id);
                ClearLive(d);
            }
            _connecting.Clear();
        }
        foreach (var l in links) l.Close();
        Log.Info("KdeConnect: endpoint stopped");
        Publish();
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            _settings.Changed -= OnSettingsChanged;
            try { NetworkChange.NetworkAddressChanged -= OnNetworkChanged; } catch { }
            _networkDebounce?.Dispose();
            if (_running) StopEndpoint();
        }
    }

    // ---------------------------------------------------------------- identity & persistence

    private void EnsureIdentity()
    {
        if (_local is not null) return;
        var dir = DataDir;
        var idPath = Path.Combine(dir, "identity.json");
        var pfxPath = Path.Combine(dir, "certificate.pfx");
        var id = JsonStore.Load(idPath, () => new IdentityRecord()).DeviceId;
        X509Certificate2? cert = null;
        if (DeviceIdentity.IsValidDeviceId(id) && File.Exists(pfxPath))
        {
            try
            {
                cert = CertificateHelper.LoadPfx(File.ReadAllBytes(pfxPath));
                if (!cert.HasPrivateKey || CertificateHelper.CommonName(cert) != id || cert.NotAfter < DateTime.Now.AddDays(30))
                {
                    cert.Dispose();
                    cert = null;
                }
            }
            catch (Exception ex) { Log.Warn("KdeConnect: stored certificate unreadable, regenerating", ex); cert = null; }
        }
        if (cert is null)
        {
            if (!DeviceIdentity.IsValidDeviceId(id)) id = Guid.NewGuid().ToString("N");
            cert = CertificateHelper.Create(id);
            var tmp = pfxPath + ".tmp";
            File.WriteAllBytes(tmp, cert.Export(X509ContentType.Pkcs12));
            File.Move(tmp, pfxPath, overwrite: true);
            JsonStore.Save(idPath, new IdentityRecord { DeviceId = id });
            Log.Info("KdeConnect: generated new device identity");
        }
        _localId = id;
        _local = new LocalInfo(id, _localName, cert);

        var paired = JsonStore.Load(Path.Combine(dir, "devices.json"), () => new List<PairedRecord>());
        lock (_gate)
        {
            foreach (var r in paired)
            {
                if (!DeviceIdentity.IsValidDeviceId(r.Id) || r.Certificate.Length == 0 || _devices.ContainsKey(r.Id)) continue;
                _devices[r.Id] = new DeviceState(r.Id)
                {
                    Name = r.Name, Type = r.Type, Pair = PhonePairState.Paired, PairedCert = r.Certificate,
                    Address = r.LastAddress, ProtocolVersion = r.ProtocolVersion,
                };
            }
        }
    }

    private void SavePaired()
    {
        List<PairedRecord> list;
        lock (_gate)
        {
            list = _devices.Values.Where(d => d.Pair == PhonePairState.Paired && d.PairedCert is not null)
                .Select(d => new PairedRecord
                {
                    Id = d.Id, Name = d.Name, Type = d.Type, Certificate = d.PairedCert!, LastAddress = d.Address,
                    ProtocolVersion = d.ProtocolVersion,
                }).ToList();
        }
        try { JsonStore.Save(Path.Combine(DataDir, "devices.json"), list); }
        catch (Exception ex) { Log.Error("KdeConnect: saving paired devices failed", ex); }
    }

    private byte[]? PinnedCertificate(string deviceId)
    {
        lock (_gate)
            return _devices.TryGetValue(deviceId, out var d) && d.Pair == PhonePairState.Paired ? d.PairedCert : null;
    }

    // ---------------------------------------------------------------- discovery

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        try
        {
            _networkDebounce ??= new Timer(_ => { if (_running) Announce(); });
            _networkDebounce.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Periodic re-announce only while a paired device is missing (or none is paired yet): phones answer every broadcast with a
    /// brand-new connection, so announcing to an already connected phone just churns links (and Android does not resend its
    /// battery state on a replaced link).
    /// </summary>
    private void PeriodicAnnounce()
    {
        bool allConnected;
        lock (_gate)
        {
            var paired = _devices.Values.Where(d => d.Pair == PhonePairState.Paired).ToList();
            allConnected = paired.Count > 0 && paired.All(d => d.IsReachable);
        }
        if (!allConnected) Announce();
    }

    /// <summary>Sends our UDP identity: broadcast on every IPv4 interface + unicast to paired devices that are offline.</summary>
    private void Announce()
    {
        try
        {
            if (!_running || _local is null) return;
            var payload = DeviceIdentity.Build(_localId, _localName, tcpPort: _tcpPort).SerializeUtf8();
            if (_options.AnnounceTargets is { } targets)
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                foreach (var t in targets) TrySend(s, payload, t);
                return;
            }

            var unicast = new List<IPAddress>();
            lock (_gate)
            {
                foreach (var d in _devices.Values)
                    if (d.Pair == PhonePairState.Paired && !d.IsReachable && IPAddress.TryParse(d.Address, out var a) && a.AddressFamily == AddressFamily.InterNetwork)
                        unicast.Add(a);
            }

            var sent = false;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                    try
                    {
                        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
                        s.Bind(new IPEndPoint(ua.Address, 0));
                        TrySend(s, payload, new IPEndPoint(IPAddress.Broadcast, _options.UdpTargetPort));
                        if (ua.IPv4Mask is { } mask && !mask.Equals(IPAddress.Any))
                        {
                            var ip = ua.Address.GetAddressBytes();
                            var m = mask.GetAddressBytes();
                            var bc = new byte[4];
                            for (var i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~m[i]);
                            TrySend(s, payload, new IPEndPoint(new IPAddress(bc), _options.UdpTargetPort));
                        }
                        sent = true;
                    }
                    catch (SocketException ex) { Log.Debug($"KdeConnect: broadcast via {ua.Address} failed: {ex.Message}"); }
                }
            }
            if (unicast.Count > 0 || !sent)
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
                if (!sent) TrySend(s, payload, new IPEndPoint(IPAddress.Broadcast, _options.UdpTargetPort));
                foreach (var a in unicast) TrySend(s, payload, new IPEndPoint(a, _options.UdpTargetPort));
            }
        }
        catch (Exception ex) { Log.Warn("KdeConnect: announce failed", ex); }
    }

    private static void TrySend(Socket s, byte[] payload, IPEndPoint target)
    {
        try { s.SendTo(payload, target); }
        catch (SocketException ex) { Log.Debug($"KdeConnect: UDP to {target} failed: {ex.SocketErrorCode}"); }
    }

    private async Task UdpLoopAsync(Socket udp, CancellationToken ct)
    {
        var buffer = new byte[DeviceIdentity.MaxIdentityPacketSize];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult r;
            try { r = await udp.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested) break;
                Log.Debug($"KdeConnect: UDP receive: {ex.SocketErrorCode}");
                continue;
            }
            try
            {
                if (r.RemoteEndPoint is not IPEndPoint sender) continue;
                var address = sender.Address.IsIPv4MappedToIPv6 ? sender.Address.MapToIPv4() : sender.Address;
                if (IPAddress.IsLoopback(address) && !_options.AcceptLoopback) continue;
                var p = NetworkPacket.TryParse(Encoding.UTF8.GetString(buffer, 0, r.ReceivedBytes));
                if (!DeviceIdentity.IsValidDiscovery(p)) continue;
                var id = p!.GetString("deviceId")!;
                if (id == _localId) continue;
                var version = p.GetInt("protocolVersion") ?? 0;
                var port = p.GetInt("tcpPort") ?? 0;
                if (version < DeviceIdentity.ProtocolVersion || port < _options.TcpPortMin || port > _options.TcpPortMax) continue;
                if (!TryBeginConnect(id)) continue;
                Task.Run(() => ConnectAsync(new IPEndPoint(address, port), id, version, ct)).Forget("KdeConnect.Connect");
            }
            catch (Exception ex) { Log.Warn("KdeConnect: bad discovery packet", ex); }
        }
    }

    private bool TryBeginConnect(string id)
    {
        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (_devices.TryGetValue(id, out var d) && d.IsReachable) return false;
            if (_connecting.TryGetValue(id, out var since) && now - since < 15_000) return false;
            _connecting[id] = now;
            return true;
        }
    }

    private async Task ConnectAsync(IPEndPoint endpoint, string id, int version, CancellationToken ct)
    {
        try
        {
            var local = _local ?? throw new InvalidOperationException("no identity");
            var link = await LinkHandshake.ConnectAsync(endpoint, id, version, local, PinnedCertificate, ct).ConfigureAwait(false);
            AttachLink(link);
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            Log.Debug($"KdeConnect: connecting to {endpoint} failed: {ex.Message}");
            // Ask the other side to connect to us instead (as kdeconnect-kde does).
            try
            {
                if (_running)
                {
                    using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    TrySend(s, DeviceIdentity.Build(_localId, _localName, tcpPort: _tcpPort).SerializeUtf8(),
                        new IPEndPoint(endpoint.Address, _options.UdpTargetPort));
                }
            }
            catch { }
        }
        finally
        {
            lock (_gate) _connecting.Remove(id);
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested) break;
                Log.Debug($"KdeConnect: accept failed: {ex.SocketErrorCode}");
                continue;
            }
            Task.Run(async () =>
            {
                try
                {
                    var local = _local ?? throw new InvalidOperationException("no identity");
                    var link = await LinkHandshake.AcceptAsync(client, local, PinnedCertificate, ct).ConfigureAwait(false);
                    AttachLink(link);
                }
                catch (Exception ex)
                {
                    client.Dispose();
                    if (!ct.IsCancellationRequested) Log.Debug($"KdeConnect: incoming connection rejected: {ex.Message}");
                }
            }, CancellationToken.None).Forget("KdeConnect.Accept");
        }
    }

    // ---------------------------------------------------------------- links

    private void AttachLink(DeviceLink link)
    {
        DeviceLink? old;
        bool paired;
        lock (_gate)
        {
            if (!_running)
            {
                link.Close();
                return;
            }
            if (!_devices.TryGetValue(link.DeviceId, out var d))
            {
                if (_devices.Values.Count(x => x.Pair != PhonePairState.Paired) >= MaxUnpairedDevices)
                {
                    link.Close();
                    return;
                }
                d = new DeviceState(link.DeviceId);
                _devices[d.Id] = d;
            }
            if (d.Pair == PhonePairState.Paired && d.PairedCert is not null && !link.PeerCertificate.RawData.AsSpan().SequenceEqual(d.PairedCert))
            {
                link.Close();
                return;
            }
            old = d.Link;
            d.Link = link;
            d.LinkGen++;
            if (link.Name.Length > 0) d.Name = link.Name;
            d.Type = link.DeviceType;
            d.ProtocolVersion = link.ProtocolVersion;
            d.Address = link.Address.ToString();
            if (old is not null && d.Pair is PhonePairState.RequestedByPeer or PhonePairState.RequestedByUs)
            {
                CancelPairTimer(d);
                d.Pair = PhonePairState.NotPaired;
                d.VerificationKey = null;
            }
            paired = d.Pair == PhonePairState.Paired;
            link.PacketReceived += OnPacket;
            link.Closed += OnLinkClosed;
        }
        old?.Close();
        link.Start();
        Log.Info($"KdeConnect: link up with {link.DeviceId[..8]}… ({link.DeviceType}, paired={paired})");
        if (paired)
        {
            SavePaired();
            RequestInitialState(link);
        }
        Publish();
    }

    private static void RequestInitialState(DeviceLink link)
    {
        link.SendAsync(new NetworkPacket(NetworkPacket.BatteryRequest, new JsonObject { ["request"] = true })).Forget("KdeConnect.Init");
        link.SendAsync(new NetworkPacket(NetworkPacket.ConnectivityRequest, new JsonObject())).Forget("KdeConnect.Init");
        link.SendAsync(new NetworkPacket(NetworkPacket.NotificationRequest, new JsonObject { ["request"] = true })).Forget("KdeConnect.Init");
    }

    private void OnLinkClosed(DeviceLink link)
    {
        int gen;
        lock (_gate)
        {
            if (!_devices.TryGetValue(link.DeviceId, out var d) || d.Link != link) return;
            d.Link = null;
            gen = ++d.LinkGen;
            CancelPairTimer(d);
            d.VerificationKey = null;
            d.PairTimestamp = 0;
            if (d.Pair != PhonePairState.Paired)
            {
                _devices.Remove(d.Id);
                ClearLive(d);
            }
        }
        Log.Info($"KdeConnect: link down {link.DeviceId[..8]}…");
        Publish();
        // Phones often drop and re-open the connection within milliseconds; keep battery / signal / notifications through
        // such a hand-over and only forget them when the device really stays away.
        var id = link.DeviceId;
        Task.Delay(_options.OfflineGrace).ContinueWith(_ =>
        {
            bool cleared = false;
            lock (_gate)
            {
                if (_devices.TryGetValue(id, out var dd) && dd.Link is null && dd.LinkGen == gen)
                {
                    ClearLive(dd);
                    cleared = true;
                }
            }
            if (cleared) Publish();
        }, TaskScheduler.Default).Forget("KdeConnect.Offline");
    }

    private static void ClearLive(DeviceState d)
    {
        d.Battery = null;
        d.Signal = null;
        d.Notifications = [];
        d.Call = null;
        d.CallGen++;
        d.VerificationKey = null;
        d.PairTimestamp = 0;
    }

    // ---------------------------------------------------------------- incoming packets

    private void OnPacket(DeviceLink link, NetworkPacket p)
    {
        DeviceState? d;
        PhonePairState state;
        lock (_gate)
        {
            if (!_devices.TryGetValue(link.DeviceId, out d) || d.Link != link) return;
            state = d.Pair;
        }
        switch (p.Type)
        {
            case NetworkPacket.Pair:
                HandlePair(d, link, p);
                return;
            case NetworkPacket.Identity:
                return;
        }
        if (state != PhonePairState.Paired)
        {
            if (state == PhonePairState.NotPaired)
            {
                Log.Debug($"KdeConnect: {p.Type} from unpaired device ignored; telling it we are not paired");
                link.SendAsync(PairPacket(false)).Forget("KdeConnect.Unpair");
            }
            return;
        }
        switch (p.Type)
        {
            case NetworkPacket.Battery: HandleBattery(d, p); break;
            case NetworkPacket.Connectivity: HandleConnectivity(d, p); break;
            case NetworkPacket.Telephony: HandleTelephony(d, p); break;
            case NetworkPacket.Notification: HandleNotification(d, link, p); break;
            case NetworkPacket.Clipboard:
            case NetworkPacket.ClipboardConnect:
                var content = p.GetString("content");
                if (string.IsNullOrEmpty(content)) break;
                if (p.Type == NetworkPacket.ClipboardConnect && (p.GetLong("timestamp") ?? 0) == 0) break;
                Raise(() => ClipboardReceived?.Invoke(this, content));
                break;
            case NetworkPacket.Ping:
                Log.Debug("KdeConnect: ping received");
                break;
            default:
                Log.Debug($"KdeConnect: unhandled packet {p.Type}");
                break;
        }
    }

    private static NetworkPacket PairPacket(bool pair, long? timestamp = null)
    {
        var body = new JsonObject { ["pair"] = pair };
        if (timestamp is { } ts) body["timestamp"] = ts;
        return new NetworkPacket(NetworkPacket.Pair, body);
    }

    private void HandlePair(DeviceState d, DeviceLink link, NetworkPacket p)
    {
        var wantsPair = p.GetBool("pair") ?? false;
        PhoneDevice? requested = null;
        var reply = (NetworkPacket?)null;
        var changedPairing = false;
        lock (_gate)
        {
            if (wantsPair)
            {
                switch (d.Pair)
                {
                    case PhonePairState.RequestedByUs:
                        CancelPairTimer(d);
                        d.Pair = PhonePairState.Paired;
                        d.PairedCert = link.PeerCertificate.RawData;
                        d.VerificationKey = null;
                        changedPairing = true;
                        Log.Info("KdeConnect: pairing accepted by the device");
                        break;
                    case PhonePairState.RequestedByPeer:
                        break;
                    case PhonePairState.Paired:
                    case PhonePairState.NotPaired:
                        if (d.Pair == PhonePairState.Paired)
                        {
                            // The device forgot us: drop the old trust and treat this as a new request.
                            d.Pair = PhonePairState.NotPaired;
                            d.PairedCert = null;
                            changedPairing = true;
                        }
                        var ts = p.GetLong("timestamp");
                        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        if (ts is { } t && Math.Abs(now - t) > AllowedPairSkewSeconds)
                        {
                            Log.Warn("KdeConnect: pair request rejected, clocks out of sync");
                            reply = PairPacket(false);
                            break;
                        }
                        d.Pair = PhonePairState.RequestedByPeer;
                        d.PairTimestamp = ts ?? 0;
                        d.VerificationKey = ComputeKey(link, d.PairTimestamp);
                        StartPairTimer(d);
                        requested = Snapshot(d);
                        break;
                }
            }
            else
            {
                if (d.Pair == PhonePairState.Paired) { changedPairing = true; Log.Info("KdeConnect: unpaired by the device"); }
                CancelPairTimer(d);
                d.Pair = PhonePairState.NotPaired;
                d.PairedCert = null;
                d.VerificationKey = null;
                d.PairTimestamp = 0;
                ClearLive(d);
            }
        }
        if (reply is not null) link.SendAsync(reply).Forget("KdeConnect.Pair");
        if (changedPairing) SavePaired();
        if (wantsPair && changedPairing && d.Pair == PhonePairState.Paired) RequestInitialState(link);
        Publish();
        if (requested is not null) Raise(() => PairRequested?.Invoke(this, requested));
    }

    private string? ComputeKey(DeviceLink link, long timestamp)
    {
        try { return _local is null ? null : CertificateHelper.VerificationKey(_local.Certificate, link.PeerCertificate, timestamp); }
        catch (Exception ex) { Log.Warn("KdeConnect: verification key failed", ex); return null; }
    }

    private void StartPairTimer(DeviceState d)
    {
        CancelPairTimer(d);
        var cts = new CancellationTokenSource();
        d.PairTimer = cts;
        Task.Delay(_options.PairTimeout, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            DeviceLink? link = null;
            lock (_gate)
            {
                if (d.PairTimer != cts || d.Pair is not (PhonePairState.RequestedByUs or PhonePairState.RequestedByPeer)) return;
                d.PairTimer = null;
                d.Pair = PhonePairState.NotPaired;
                d.VerificationKey = null;
                d.PairTimestamp = 0;
                link = d.Link;
            }
            Log.Info("KdeConnect: pairing timed out");
            link?.SendAsync(PairPacket(false)).Forget("KdeConnect.PairTimeout");
            Publish();
        }, TaskScheduler.Default);
    }

    private static void CancelPairTimer(DeviceState d)
    {
        var t = d.PairTimer;
        d.PairTimer = null;
        try { t?.Cancel(); t?.Dispose(); } catch { }
    }

    private void HandleBattery(DeviceState d, NetworkPacket p)
    {
        var charge = p.GetInt("currentCharge") ?? -1;
        if (charge < 0) return;
        charge = Math.Clamp(charge, 0, 100);
        var charging = p.GetBool("isCharging") ?? false;
        var low = p.GetInt("thresholdEvent") == 1 || (!charging && charge <= 15);
        lock (_gate) d.Battery = new PhoneBattery(charge, charging, low);
        Publish();
    }

    private void HandleConnectivity(DeviceState d, NetworkPacket p)
    {
        PhoneSignal? signal = null;
        if (p.Body["signalStrengths"] is JsonObject subs)
        {
            foreach (var kv in subs.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Value is not JsonObject o) continue;
                var type = NetworkPacket.AsString(o["networkType"]);
                var strength = (int)Math.Clamp(NetworkPacket.AsLong(o["signalStrength"]) ?? 0, 0, 4);
                signal = new PhoneSignal(string.IsNullOrWhiteSpace(type) ? "Unknown" : type, strength);
                break;
            }
        }
        lock (_gate) d.Signal = signal;
        Publish();
    }

    private void HandleNotification(DeviceState d, DeviceLink link, NetworkPacket p)
    {
        var id = p.GetString("id");
        if (string.IsNullOrEmpty(id)) return;
        if (p.GetBool("isCancel") == true)
        {
            lock (_gate) d.Notifications.RemoveAll(n => n.Id == id);
            Publish();
            return;
        }
        var title = p.GetString("title") ?? "";
        var text = p.GetString("text") ?? "";
        var ticker = p.GetString("ticker");
        if (title.Length == 0 && text.Length == 0 && !string.IsNullOrEmpty(ticker)) text = ticker;
        var ms = p.GetLong("time") ?? 0;
        DateTimeOffset time;
        try { time = ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime() : DateTimeOffset.Now; }
        catch (ArgumentOutOfRangeException) { time = DateTimeOffset.Now; }
        var replyId = p.GetString("requestReplyId");
        var silent = p.GetBool("silent") ?? false;

        bool isNew;
        PhoneNotification notification;
        lock (_gate)
        {
            var existing = d.Notifications.FindIndex(n => n.Id == id);
            isNew = existing < 0;
            notification = new PhoneNotification(id, p.GetString("appName") ?? "", title, text, string.IsNullOrEmpty(ticker) ? null : ticker,
                time, p.GetBool("isClearable") ?? true, silent, string.IsNullOrEmpty(replyId) ? null : replyId,
                p.GetStringList("actions"), isNew ? null : d.Notifications[existing].IconPng);
            if (!isNew) d.Notifications.RemoveAt(existing);
            d.Notifications.Add(notification);
            d.Notifications.Sort((a, b) => b.Time.CompareTo(a.Time));
            if (d.Notifications.Count > MaxNotifications) d.Notifications.RemoveRange(MaxNotifications, d.Notifications.Count - MaxNotifications);
        }
        Publish();
        var announce = isNew && !silent;
        if (p.HasPayload && _local is { } local)
        {
            DownloadIconAsync(d, link, local, notification, p.PayloadPort!.Value, p.PayloadSize, announce).Forget("KdeConnect.Icon");
        }
        else if (announce)
        {
            Raise(() => NotificationPosted?.Invoke(this, new PhoneNotificationEventArgs(notification)));
        }
    }

    private async Task DownloadIconAsync(DeviceState d, DeviceLink link, LocalInfo local, PhoneNotification n, int port, long size, bool announce)
    {
        byte[]? icon = null;
        try { icon = await link.DownloadPayloadAsync(port, size, local, MaxIconBytes, _runCts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { Log.Debug($"KdeConnect: icon download failed: {ex.Message}"); }
        PhoneNotification? current = null;
        lock (_gate)
        {
            var i = d.Notifications.FindIndex(x => x.Id == n.Id);
            if (i >= 0)
            {
                if (icon is not null) d.Notifications[i] = d.Notifications[i] with { IconPng = icon };
                current = d.Notifications[i];
            }
        }
        if (current is null) return;
        if (icon is not null) Publish();
        if (announce) Raise(() => NotificationPosted?.Invoke(this, new PhoneNotificationEventArgs(current)));
    }

    /// <summary>
    /// kdeconnect.telephony (Android TelephonyPlugin): {event: ringing|talking|missedCall|sms, contactName, phoneNumber,
    /// phoneThumbnail (base64), isCancel}. When a call ends Android first sends "missedCall" (if it was never answered) and then
    /// re-sends the last ringing/talking packet with isCancel = true, so such a cancel must not wipe the fresh missed call.
    /// </summary>
    private void HandleTelephony(DeviceState d, NetworkPacket p)
    {
        var ev = p.GetString("event") ?? "";
        if (ev is not ("ringing" or "talking" or "missedCall")) return; // "sms" = legacy SMS packets, ignored
        var cancel = p.GetBool("isCancel") ?? false;
        var number = p.GetString("phoneNumber");
        var name = p.GetString("contactName");
        if (string.IsNullOrWhiteSpace(number)) number = null;
        if (string.IsNullOrWhiteSpace(name)) name = null;
        var scheduleGen = -1;
        lock (_gate)
        {
            var current = d.Call;
            if (cancel)
            {
                if (current is null) return;
                if (current.State == PhoneCallState.Missed && ev != "missedCall") return;
                d.Call = null;
                d.CallGen++;
            }
            else
            {
                var same = SameCaller(current, name, number);
                var photo = DecodeThumbnail(p.GetString("phoneThumbnail")) ?? (same ? current!.ContactPhoto : null);
                var state = ev switch { "ringing" => PhoneCallState.Ringing, "talking" => PhoneCallState.Talking, _ => PhoneCallState.Missed };
                var since = same && current!.State == state ? current.Since : DateTimeOffset.Now;
                d.Call = new PhoneCall(state, name ?? number, number, since, photo);
                d.CallGen++;
                if (state == PhoneCallState.Missed) scheduleGen = d.CallGen;
            }
        }
        Publish();
        if (scheduleGen < 0) return;
        Task.Delay(_options.MissedCallLinger).ContinueWith(_ =>
        {
            var cleared = false;
            lock (_gate)
            {
                if (d.CallGen == scheduleGen && d.Call is { State: PhoneCallState.Missed })
                {
                    d.Call = null;
                    d.CallGen++;
                    cleared = true;
                }
            }
            if (cleared) Publish();
        }, TaskScheduler.Default).Forget("KdeConnect.MissedCall");
    }

    private static bool SameCaller(PhoneCall? c, string? name, string? number) =>
        c is not null && (number is not null ? c.PhoneNumber == number : c.ContactName == name);

    private static byte[]? DecodeThumbnail(string? b64)
    {
        if (string.IsNullOrEmpty(b64) || b64.Length > MaxIconBytes * 4 / 3 + 8) return null;
        try { return Convert.FromBase64String(b64.Trim()); }
        catch (FormatException) { return null; }
    }

    // ---------------------------------------------------------------- snapshots

    private PhoneDevice Snapshot(DeviceState d) =>
        new(d.Id, d.Name.Length > 0 ? d.Name : d.Id[..8], d.Type, d.IsReachable, d.Pair, d.Address, d.VerificationKey, d.ProtocolVersion);

    private DeviceState? ActiveState() =>
        _devices.Values.Where(d => d.Pair == PhonePairState.Paired)
            .OrderByDescending(d => d.IsReachable).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private void Publish()
    {
        lock (_gate)
        {
            _snapDevices = _devices.Values
                .OrderByDescending(d => d.Pair == PhonePairState.Paired).ThenByDescending(d => d.IsReachable)
                .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal)
                .Select(Snapshot).ToArray();
            var active = ActiveState();
            _snapActive = active is null ? null : Snapshot(active);
            _snapBattery = active?.Battery;
            _snapSignal = active?.Signal;
            _snapNotifications = active is null ? [] : active.Notifications.ToArray();
            _snapCall = active?.Call;
        }
        Raise(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private static void Raise(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error("KdeConnect: event handler failed", ex); }
    }

    // ---------------------------------------------------------------- commands

    private DeviceLink? ActiveLink()
    {
        lock (_gate)
        {
            var a = ActiveState();
            return a is { IsReachable: true } ? a.Link : null;
        }
    }

    private async Task<bool> SendActiveAsync(NetworkPacket p)
    {
        try
        {
            var link = ActiveLink();
            return link is not null && await link.SendAsync(p).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Error($"KdeConnect: sending {p.Type} failed", ex); return false; }
    }

    public Task RefreshAsync()
    {
        try
        {
            if (_running) Task.Run(Announce).Forget("KdeConnect.Announce");
            if (ActiveLink() is { } link)
            {
                link.SendAsync(new NetworkPacket(NetworkPacket.BatteryRequest, new JsonObject { ["request"] = true })).Forget("KdeConnect.Refresh");
                link.SendAsync(new NetworkPacket(NetworkPacket.ConnectivityRequest, new JsonObject())).Forget("KdeConnect.Refresh");
            }
        }
        catch (Exception ex) { Log.Error("KdeConnect: refresh failed", ex); }
        return Task.CompletedTask;
    }

    /// <summary>Sends a pair request; returns true when the request was sent (or the device is already paired).</summary>
    public async Task<bool> RequestPairAsync(string deviceId)
    {
        DeviceLink? link;
        long ts;
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var d) || d.Link is not { IsClosed: false } l) return false;
            if (d.Pair == PhonePairState.Paired) return true;
            if (d.Pair == PhonePairState.RequestedByPeer) { link = null; ts = 0; }
            else
            {
                link = l;
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                d.Pair = PhonePairState.RequestedByUs;
                d.PairTimestamp = ts;
                d.VerificationKey = ComputeKey(l, ts);
                StartPairTimer(d);
            }
        }
        if (link is null)
        {
            await AcceptPairAsync(deviceId).ConfigureAwait(false);
            return true;
        }
        Publish();
        var ok = await link.SendAsync(PairPacket(true, ts)).ConfigureAwait(false);
        if (!ok)
        {
            lock (_gate)
                if (_devices.TryGetValue(deviceId, out var d) && d.Pair == PhonePairState.RequestedByUs) { CancelPairTimer(d); d.Pair = PhonePairState.NotPaired; d.VerificationKey = null; }
            Publish();
        }
        return ok;
    }

    public async Task AcceptPairAsync(string deviceId)
    {
        DeviceLink? link;
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var d) || d.Pair != PhonePairState.RequestedByPeer || d.Link is not { IsClosed: false } l) return;
            CancelPairTimer(d);
            d.Pair = PhonePairState.Paired;
            d.PairedCert = l.PeerCertificate.RawData;
            d.VerificationKey = null;
            link = l;
        }
        SavePaired();
        Publish();
        Log.Info("KdeConnect: pairing accepted");
        if (await link.SendAsync(PairPacket(true)).ConfigureAwait(false)) RequestInitialState(link);
    }

    public async Task RejectPairAsync(string deviceId)
    {
        DeviceLink? link;
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var d) || d.Pair is not (PhonePairState.RequestedByPeer or PhonePairState.RequestedByUs)) return;
            CancelPairTimer(d);
            d.Pair = PhonePairState.NotPaired;
            d.VerificationKey = null;
            d.PairTimestamp = 0;
            link = d.Link;
        }
        Publish();
        if (link is not null) await link.SendAsync(PairPacket(false)).ConfigureAwait(false);
    }

    public async Task UnpairAsync(string deviceId)
    {
        DeviceLink? link;
        bool wasPaired;
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var d)) return;
            wasPaired = d.Pair == PhonePairState.Paired;
            CancelPairTimer(d);
            d.Pair = PhonePairState.NotPaired;
            d.PairedCert = null;
            d.VerificationKey = null;
            d.PairTimestamp = 0;
            ClearLive(d);
            link = d.Link is { IsClosed: false } l ? l : null;
            if (link is null) _devices.Remove(deviceId);
        }
        if (wasPaired) SavePaired();
        Publish();
        if (link is not null) await link.SendAsync(PairPacket(false)).ConfigureAwait(false);
    }

    public Task RingAsync() => SendActiveAsync(new NetworkPacket(NetworkPacket.FindMyPhone, new JsonObject()));

    public Task PingAsync(string? message = null)
    {
        var body = new JsonObject();
        if (!string.IsNullOrEmpty(message)) body["message"] = message;
        return SendActiveAsync(new NetworkPacket(NetworkPacket.Ping, body));
    }

    public async Task SendClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        List<DeviceLink> links;
        lock (_gate)
            links = _devices.Values.Where(d => d.Pair == PhonePairState.Paired && d.IsReachable).Select(d => d.Link!).ToList();
        foreach (var l in links)
            await l.SendAsync(new NetworkPacket(NetworkPacket.Clipboard, new JsonObject { ["content"] = text })).ConfigureAwait(false);
    }

    public async Task DismissNotificationAsync(string notificationId)
    {
        if (string.IsNullOrEmpty(notificationId)) return;
        lock (_gate) ActiveState()?.Notifications.RemoveAll(n => n.Id == notificationId);
        Publish();
        await SendActiveAsync(new NetworkPacket(NetworkPacket.NotificationRequest, new JsonObject { ["cancel"] = notificationId })).ConfigureAwait(false);
    }

    public Task ReplyToNotificationAsync(string replyId, string message)
    {
        if (string.IsNullOrEmpty(replyId) || string.IsNullOrEmpty(message)) return Task.CompletedTask;
        return SendActiveAsync(new NetworkPacket(NetworkPacket.NotificationReply,
            new JsonObject { ["requestReplyId"] = replyId, ["message"] = message }));
    }

    public Task InvokeNotificationActionAsync(string notificationId, string action)
    {
        if (string.IsNullOrEmpty(notificationId) || string.IsNullOrEmpty(action)) return Task.CompletedTask;
        return SendActiveAsync(new NetworkPacket(NetworkPacket.NotificationAction,
            new JsonObject { ["key"] = notificationId, ["action"] = action }));
    }

    public Task MuteRingerAsync() =>
        SendActiveAsync(new NetworkPacket(NetworkPacket.TelephonyRequestMute, new JsonObject { ["action"] = "mute" }));

    public Task RequestNotificationsAsync() =>
        SendActiveAsync(new NetworkPacket(NetworkPacket.NotificationRequest, new JsonObject { ["request"] = true }));
}
