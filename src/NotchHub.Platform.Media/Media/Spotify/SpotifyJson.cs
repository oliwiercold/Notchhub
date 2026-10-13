using System.Text.Json;

namespace NotchHub.Platform.Media.Media.Spotify;

/// <summary>Token response of accounts.spotify.com.</summary>
internal sealed record SpotifyToken(string AccessToken, string? RefreshToken, int ExpiresIn, string? Scope);

/// <summary>Subset of GET /v1/me/player.</summary>
internal sealed record SpotifyPlayback(
    string? ItemUri, string ItemName, string Artists, bool IsPlaying, bool? Shuffle, bool? SmartShuffle, string? Repeat);

/// <summary>Tolerant parsers for the few Spotify payloads NotchHub reads (System.Text.Json, no reflection).</summary>
internal static class SpotifyJson
{
    public static SpotifyToken ParseToken(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = Str(root, "access_token");
        if (string.IsNullOrEmpty(access)) throw new FormatException("no access_token");
        var expires = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 3600;
        return new SpotifyToken(access, NullIfEmpty(Str(root, "refresh_token")), expires, Str(root, "scope"));
    }

    /// <summary>OAuth error code ("invalid_grant", "invalid_client"…) of an accounts-service error body, or null.</summary>
    public static string? ParseOAuthError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                ? err.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Web API error: {"error":{"status":403,"message":"…","reason":"PREMIUM_REQUIRED"}} → reason (may be null).</summary>
    public static string? ParseApiReason(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                return NullIfEmpty(Str(err, "reason"));
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>GET /v1/me → display name (falls back to the user id).</summary>
    public static string? ParseProfileName(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return NullIfEmpty(Str(doc.RootElement, "display_name")) ?? NullIfEmpty(Str(doc.RootElement, "id"));
    }

    /// <summary>GET /v1/me/player (or /currently-playing). Empty body (204) → null.</summary>
    public static SpotifyPlayback? ParsePlayback(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        string? uri = null, name = "", artists = "";
        if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            uri = NullIfEmpty(Str(item, "uri"));
            name = Str(item, "name") ?? "";
            if (item.TryGetProperty("artists", out var arr) && arr.ValueKind == JsonValueKind.Array)
                artists = string.Join(", ", arr.EnumerateArray().Select(a => Str(a, "name")).Where(n => !string.IsNullOrEmpty(n)));
            else if (item.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
                artists = Str(show, "name") ?? "";
        }
        return new SpotifyPlayback(uri, name ?? "", artists ?? "", Bool(root, "is_playing") ?? false,
            Bool(root, "shuffle_state"), Bool(root, "smart_shuffle"), NullIfEmpty(Str(root, "repeat_state")));
    }

    /// <summary>GET /v1/me/library/contains → [true, false…].</summary>
    public static bool[] ParseContains(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("contains: not an array");
        return doc.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.True).ToArray();
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
