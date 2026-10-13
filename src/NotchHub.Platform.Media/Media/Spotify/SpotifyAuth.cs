using System.Security.Cryptography;
using System.Text;

namespace NotchHub.Platform.Media.Media.Spotify;

/// <summary>
/// OAuth 2.0 Authorization Code + PKCE helpers for the Spotify accounts service (no client secret).
/// Redirect URI rules (developer.spotify.com, validations enforced since 2025): loopback must be an explicit IP literal
/// (<c>http://127.0.0.1:PORT</c>; "localhost" is rejected), plain http is allowed only for loopback, and the URI must match the one
/// registered in the dashboard exactly — so NotchHub uses one fixed port/path the user registers once.
/// </summary>
internal static class SpotifyAuth
{
    /// <summary>Fixed loopback port. Keep in sync with the redirect URI shown in Settings (NotchHub.Features.Settings).</summary>
    public const int RedirectPort = 43819;
    public const string RedirectPath = "/callback";
    public static readonly string RedirectUri = $"http://127.0.0.1:{RedirectPort}{RedirectPath}";

    public const string AuthorizeEndpoint = "https://accounts.spotify.com/authorize";
    public const string TokenEndpoint = "https://accounts.spotify.com/api/token";

    /// <summary>Playback state + shuffle/repeat (Premium) + "Liked Songs" read/write.</summary>
    public const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing user-library-read user-library-modify";

    private const string Unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    /// <summary>RFC 7636 code verifier: 43–128 chars from the unreserved set.</summary>
    public static string CreateVerifier(int length = 64)
    {
        if (length is < 43 or > 128) throw new ArgumentOutOfRangeException(nameof(length));
        return RandomNumberGenerator.GetString(Unreserved, length);
    }

    /// <summary>S256 challenge: BASE64URL(SHA256(ASCII(verifier))) without padding.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(18));

    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string AuthorizeUrl(string clientId, string challenge, string state) =>
        AuthorizeEndpoint
        + "?response_type=code"
        + "&client_id=" + Uri.EscapeDataString(clientId)
        + "&scope=" + Uri.EscapeDataString(Scopes)
        + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
        + "&code_challenge_method=S256"
        + "&code_challenge=" + Uri.EscapeDataString(challenge)
        + "&state=" + Uri.EscapeDataString(state);

    /// <summary>Form body of the code → token exchange.</summary>
    public static Dictionary<string, string> CodeExchangeForm(string clientId, string code, string verifier) => new()
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = RedirectUri,
        ["client_id"] = clientId,
        ["code_verifier"] = verifier,
    };

    public static Dictionary<string, string> RefreshForm(string clientId, string refreshToken) => new()
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = refreshToken,
        ["client_id"] = clientId,
    };

    /// <summary>Result of the browser redirect to the loopback listener.</summary>
    public sealed record Callback(string? Code, string? State, string? Error);

    /// <summary>
    /// Parses the request target of the redirect ("/callback?code=…&amp;state=…"). Returns null when the path is not
    /// <see cref="RedirectPath"/> (favicon, probes) so the listener keeps waiting.
    /// </summary>
    public static Callback? ParseCallback(string requestTarget)
    {
        if (string.IsNullOrEmpty(requestTarget)) return null;
        var q = requestTarget.IndexOf('?');
        var path = q < 0 ? requestTarget : requestTarget[..q];
        if (!string.Equals(path, RedirectPath, StringComparison.Ordinal)) return null;
        string? code = null, state = null, error = null;
        if (q >= 0)
        {
            foreach (var pair in requestTarget[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair[..eq];
                var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                switch (key)
                {
                    case "code": code = value; break;
                    case "state": state = value; break;
                    case "error": error = value; break;
                }
            }
        }
        return new Callback(code, state, error);
    }

    /// <summary>Request target from an HTTP/1.x request line ("GET /callback?… HTTP/1.1"), or null.</summary>
    public static string? RequestTarget(string requestLine)
    {
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
    }
}
