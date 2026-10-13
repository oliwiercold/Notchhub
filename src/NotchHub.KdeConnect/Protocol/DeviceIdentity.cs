using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NotchHub.KdeConnect.Protocol;

/// <summary>Identity packet construction and validation (rules mirror kdeconnect-kde core/deviceinfo.h).</summary>
internal static partial class DeviceIdentity
{
    public const int ProtocolVersion = 8;
    public const int MaxIdentityPacketSize = 8192;

    public static readonly string[] IncomingCapabilities =
    [
        NetworkPacket.Battery, NetworkPacket.Notification, NetworkPacket.Connectivity,
        NetworkPacket.Clipboard, NetworkPacket.ClipboardConnect, NetworkPacket.Ping, NetworkPacket.Telephony,
    ];

    public static readonly string[] OutgoingCapabilities =
    [
        NetworkPacket.BatteryRequest, NetworkPacket.NotificationRequest, NetworkPacket.NotificationReply,
        NetworkPacket.NotificationAction, NetworkPacket.FindMyPhone, NetworkPacket.Ping,
        NetworkPacket.Clipboard, NetworkPacket.ConnectivityRequest, NetworkPacket.TelephonyRequestMute,
    ];

    [GeneratedRegex("^[a-zA-Z0-9_-]{32,38}$")]
    private static partial Regex DeviceIdRegex();

    [GeneratedRegex(@"[""',;:.!?()\[\]<>]")]
    private static partial Regex NameInvalidChars();

    public static bool IsValidDeviceId(string? id) => id is not null && DeviceIdRegex().IsMatch(id);

    /// <summary>Removes characters KDE Connect does not allow in device names and truncates to 32 chars.</summary>
    public static string FilterName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var s = NameInvalidChars().Replace(name, "").Trim();
        return s.Length > 32 ? s[..32].Trim() : s;
    }

    /// <summary>Full identity (as sent over TLS): deviceId, deviceName, deviceType, protocolVersion, capabilities.</summary>
    public static bool IsValidIdentity(NetworkPacket? p) =>
        p is not null && p.Type == NetworkPacket.Identity
        && FilterName(p.GetString("deviceName")).Length > 0
        && IsValidDeviceId(p.GetString("deviceId"));

    /// <summary>UDP discovery / plain-text TCP packet: only a valid deviceId is required.</summary>
    public static bool IsValidDiscovery(NetworkPacket? p) =>
        p is not null && p.Type == NetworkPacket.Identity && IsValidDeviceId(p.GetString("deviceId"));

    /// <summary>Builds our identity packet. tcpPort (UDP) and target fields (plain-text TCP, protocol v8 "connection packet") are optional.</summary>
    public static NetworkPacket Build(string deviceId, string deviceName, int? tcpPort = null, string? targetDeviceId = null, int? targetProtocolVersion = null,
        string deviceType = "desktop")
    {
        var body = new JsonObject
        {
            ["deviceId"] = deviceId,
            ["deviceName"] = deviceName,
            ["deviceType"] = deviceType,
            ["protocolVersion"] = ProtocolVersion,
            ["incomingCapabilities"] = new JsonArray(IncomingCapabilities.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["outgoingCapabilities"] = new JsonArray(OutgoingCapabilities.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
        };
        if (tcpPort is { } port) body["tcpPort"] = port;
        if (targetDeviceId is not null) body["targetDeviceId"] = targetDeviceId;
        if (targetProtocolVersion is { } tpv) body["targetProtocolVersion"] = tpv;
        return new NetworkPacket(NetworkPacket.Identity, body);
    }

    public static string NormalizeType(string? type) => type switch
    {
        "smartphone" or "phone" => "phone",
        "tablet" => "tablet",
        "desktop" => "desktop",
        "laptop" => "laptop",
        "tv" => "tv",
        _ => "phone",
    };
}
