using System.Net;

namespace NotchHub.KdeConnect;

/// <summary>Internal knobs (tests / harness). Production uses the defaults.</summary>
internal sealed class KdeConnectOptions
{
    /// <summary>Address the UDP and TCP listeners bind to (tests: 127.0.0.1).</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;
    /// <summary>UDP discovery listen port (0 = ephemeral, tests only).</summary>
    public int UdpPort { get; init; } = 1716;
    /// <summary>Destination port of announcements / reverse-connection requests.</summary>
    public int UdpTargetPort { get; init; } = 1716;
    public int TcpPortMin { get; init; } = 1716;
    public int TcpPortMax { get; init; } = 1764;
    /// <summary>Data directory (identity, certificate, paired devices). Null = AppPaths.Dir("kdeconnect").</summary>
    public string? DataDirectory { get; init; }
    /// <summary>When set, announcements go only to these endpoints (no broadcast, no interface enumeration).</summary>
    public IReadOnlyList<IPEndPoint>? AnnounceTargets { get; init; }
    /// <summary>Accept UDP discovery packets from loopback senders (tests).</summary>
    public bool AcceptLoopback { get; init; }
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan PairTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>
    /// An unpaired link with no pair request in progress is closed after this long, so idle strangers cannot fill the
    /// unpaired-device slots. A real phone reconnects on our next announcement.
    /// </summary>
    public TimeSpan UnpairedLinkTimeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>How long battery / signal / notifications survive a dropped link (phones hand links over constantly).</summary>
    public TimeSpan OfflineGrace { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>How long a missed call stays visible as <see cref="NotchHub.Core.Contracts.IPhoneService.ActiveCall"/>.</summary>
    public TimeSpan MissedCallLinger { get; init; } = TimeSpan.FromMinutes(1);
}
