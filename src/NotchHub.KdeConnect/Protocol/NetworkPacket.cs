using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NotchHub.KdeConnect.Protocol;

/// <summary>A KDE Connect packet: one JSON object per line.</summary>
internal sealed class NetworkPacket
{
    public const string Identity = "kdeconnect.identity";
    public const string Pair = "kdeconnect.pair";
    public const string Battery = "kdeconnect.battery";
    public const string BatteryRequest = "kdeconnect.battery.request";
    public const string Notification = "kdeconnect.notification";
    public const string NotificationRequest = "kdeconnect.notification.request";
    public const string NotificationReply = "kdeconnect.notification.reply";
    public const string NotificationAction = "kdeconnect.notification.action";
    public const string Connectivity = "kdeconnect.connectivity_report";
    public const string ConnectivityRequest = "kdeconnect.connectivity_report.request";
    public const string FindMyPhone = "kdeconnect.findmyphone.request";
    public const string Ping = "kdeconnect.ping";
    public const string Clipboard = "kdeconnect.clipboard";
    public const string ClipboardConnect = "kdeconnect.clipboard.connect";
    public const string Telephony = "kdeconnect.telephony";
    public const string TelephonyRequestMute = "kdeconnect.telephony.request_mute";

    public NetworkPacket(string type, JsonObject? body = null, long? id = null)
    {
        Type = type;
        Body = body ?? new JsonObject();
        Id = id ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public long Id { get; }
    public string Type { get; }
    public JsonObject Body { get; }
    public long PayloadSize { get; set; }
    public int? PayloadPort { get; set; }

    public bool HasPayload => PayloadSize > 0 && PayloadPort is > 0 and < 65536;

    public string Serialize()
    {
        var root = new JsonObject
        {
            ["id"] = Id,
            ["type"] = Type,
            ["body"] = JsonNode.Parse(Body.ToJsonString()),
        };
        if (PayloadSize != 0) root["payloadSize"] = PayloadSize;
        if (PayloadPort is { } port) root["payloadTransferInfo"] = new JsonObject { ["port"] = port };
        return root.ToJsonString() + "\n";
    }

    public byte[] SerializeUtf8() => Encoding.UTF8.GetBytes(Serialize());

    public static NetworkPacket? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            if (JsonNode.Parse(line) is not JsonObject root) return null;
            var type = AsString(root["type"]);
            if (string.IsNullOrEmpty(type)) return null;
            var body = root["body"] as JsonObject ?? new JsonObject();
            body = (JsonObject)JsonNode.Parse(body.ToJsonString())!;
            var p = new NetworkPacket(type, body, AsLong(root["id"]) ?? 0)
            {
                PayloadSize = AsLong(root["payloadSize"]) ?? 0,
            };
            if (root["payloadTransferInfo"] is JsonObject pti && AsLong(pti["port"]) is { } port) p.PayloadPort = (int)port;
            return p;
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // ---- lenient body accessors (numbers may arrive as strings and vice versa) ----

    public string? GetString(string key) => AsString(Body[key]);
    public long? GetLong(string key) => AsLong(Body[key]);
    public int? GetInt(string key) => AsLong(Body[key]) is { } l ? (int)Math.Clamp(l, int.MinValue, int.MaxValue) : null;

    public bool? GetBool(string key)
    {
        var n = Body[key];
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<bool>(out var b)) return b;
        var s = AsString(n);
        if (bool.TryParse(s, out b)) return b;
        return AsLong(n) is { } l ? l != 0 : null;
    }

    public bool Has(string key) => Body.ContainsKey(key);

    public IReadOnlyList<string> GetStringList(string key)
    {
        if (Body[key] is not JsonArray arr) return [];
        var list = new List<string>(arr.Count);
        foreach (var n in arr) if (AsString(n) is { Length: > 0 } s) list.Add(s);
        return list;
    }

    internal static string? AsString(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<string>(out var s)) return s;
        return v.GetValueKind() switch
        {
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
            _ => null,
        };
    }

    internal static long? AsLong(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d) && !double.IsNaN(d)) return (long)d;
        if (v.TryGetValue<string>(out var s))
        {
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && !double.IsNaN(d)) return (long)d;
        }
        if (v.GetValueKind() == JsonValueKind.Number)
        {
            // Locally built values (e.g. JsonValue<int>) do not convert via TryGetValue<long>.
            var text = v.ToJsonString();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && !double.IsNaN(d)) return (long)d;
        }
        return null;
    }
}
