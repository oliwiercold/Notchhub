using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NotchHub.Services.Calendar;

/// <summary>Result of a token endpoint call (either tokens or an OAuth error code).</summary>
internal sealed record GoogleTokenResponse(string? AccessToken, int ExpiresInSeconds, string? RefreshToken, string? IdToken, string? Error);

/// <summary>Result of the loopback redirect: the authorization code or an error code.</summary>
internal sealed record LoopbackResult(string? Code, string? Error);

/// <summary>
/// OAuth 2.0 for installed apps (RFC 8252): PKCE S256 helpers, the authorization URL, token response parsing and a
/// tiny loopback HTTP receiver on 127.0.0.1 (a <see cref="TcpListener"/>, so no URL ACL is needed).
/// </summary>
internal static class GoogleOAuth
{
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    public const string Scope = "https://www.googleapis.com/auth/calendar openid email";

    /// <summary>A high-entropy code verifier: 64 unreserved characters (RFC 7636 §4.1 allows 43–128).</summary>
    public static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(48));

    /// <summary>BASE64URL(SHA256(ASCII(verifier))) — the S256 code challenge.</summary>
    public static string CreateCodeChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Random anti-CSRF state value.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    public static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }

    public static string BuildAuthUrl(string clientId, string redirectUri, string codeChallenge, string state, string? loginHint = null)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("redirect_uri", redirectUri),
            new("response_type", "code"),
            new("scope", Scope),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
            new("state", state),
            new("access_type", "offline"),
            new("prompt", "consent"),
        };
        if (!string.IsNullOrEmpty(loginHint)) query.Add(new("login_hint", loginHint));
        return AuthEndpoint + "?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    /// <summary>Parses a JSON body of the token endpoint (success or error).</summary>
    public static GoogleTokenResponse ParseTokenResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Token response is not an object.");
        var error = Str(root, "error");
        var expires = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 3600;
        return new GoogleTokenResponse(Str(root, "access_token"), expires, Str(root, "refresh_token"), Str(root, "id_token"), error);
    }

    /// <summary>Reads the "email" claim of an id_token (the signature is not checked: it came straight from Google over TLS).</summary>
    public static string? EmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            using var doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            return Str(doc.RootElement, "email");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses "a=1&amp;b=x%20y" (with or without a leading '?').</summary>
    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((i < 0 ? pair : pair[..i]).Replace('+', ' '));
            var value = i < 0 ? "" : Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }
        return result;
    }

    /// <summary>Maps the redirect query to a code (state checked) or an error code.</summary>
    public static LoopbackResult EvaluateRedirect(IReadOnlyDictionary<string, string> query, string expectedState)
    {
        if (query.TryGetValue("error", out var error)) return new(null, string.IsNullOrEmpty(error) ? "auth_failed" : error);
        if (!query.TryGetValue("state", out var state) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
            return new(null, "state_mismatch");
        return query.TryGetValue("code", out var code) && !string.IsNullOrEmpty(code) ? new(code, null) : new(null, "no_code");
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ───────────────────────────── loopback receiver ─────────────────────────────

    /// <summary>A one-shot HTTP receiver on http://127.0.0.1:&lt;free port&gt;/.</summary>
    internal sealed class LoopbackReceiver : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackReceiver()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public string RedirectUri => $"http://127.0.0.1:{Port}/";

        /// <summary>
        /// Waits for the browser to hit the redirect URI, answers it with <paramref name="html"/> (chosen from the result)
        /// and returns the parsed query. Requests for other paths (favicon) get a 404 and are ignored.
        /// </summary>
        public async Task<Dictionary<string, string>> WaitAsync(Func<Dictionary<string, string>, string> html, CancellationToken ct)
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.ReceiveTimeout = 5000;
                using var stream = client.GetStream();
                string? target;
                try
                {
                    target = await ReadRequestTargetAsync(stream, ct).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    continue;
                }
                if (target is null) continue;

                var q = target.IndexOf('?');
                var path = q < 0 ? target : target[..q];
                if (path != "/")
                {
                    await WriteAsync(stream, "404 Not Found", "", ct).ConfigureAwait(false);
                    continue;
                }
                var query = ParseQuery(q < 0 ? "" : target[q..]);
                if (!query.ContainsKey("code") && !query.ContainsKey("error"))
                {
                    await WriteAsync(stream, "404 Not Found", "", ct).ConfigureAwait(false);
                    continue;
                }
                await WriteAsync(stream, "200 OK", html(query), ct).ConfigureAwait(false);
                return query;
            }
        }

        private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[8192];
            var total = 0;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (total < buffer.Length)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return null;
                }
                if (read == 0) break;
                total += read;
                var text = Encoding.ASCII.GetString(buffer, 0, total);
                var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
                if (lineEnd < 0) continue;
                // "GET /?code=…&state=… HTTP/1.1"
                var parts = text[..lineEnd].Split(' ');
                return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
            }
            return null;
        }

        private static async Task WriteAsync(NetworkStream stream, string status, string html, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
            try
            {
                await stream.WriteAsync(head, ct).ConfigureAwait(false);
                await stream.WriteAsync(body, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Browser went away — nothing to do.
            }
        }

        public void Dispose() => _listener.Stop();
    }

    /// <summary>The page shown in the browser after the redirect (Polish or English, following the app language).</summary>
    public static string ResultPage(bool success, string language)
    {
        var pl = !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var (title, text) = (success, pl) switch
        {
            (true, true) => ("Połączono z Google", "NotchHub ma dostęp do Twojego Kalendarza Google. Możesz zamknąć tę kartę."),
            (true, false) => ("Connected to Google", "NotchHub can now access your Google Calendar. You can close this tab."),
            (false, true) => ("Nie udało się połączyć", "Logowanie zostało przerwane lub odrzucone. Wróć do NotchHub i spróbuj ponownie. Możesz zamknąć tę kartę."),
            (false, false) => ("Could not connect", "Sign-in was cancelled or denied. Go back to NotchHub and try again. You can close this tab."),
        };
        var color = success ? "#1E8E3E" : "#D93025";
        return $$"""
            <!doctype html><html lang="{{(pl ? "pl" : "en")}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>NotchHub</title><style>
            body{margin:0;min-height:100vh;display:grid;place-items:center;background:#F3EDF7;color:#1D1B20;font-family:"Segoe UI Variable","Segoe UI",sans-serif}
            @media (prefers-color-scheme:dark){body{background:#141218;color:#E6E0E9}.card{background:#211F26!important} }
            .card{background:#FFFFFF;border-radius:28px;padding:40px 48px;max-width:440px;text-align:center;box-shadow:0 2px 12px rgba(0,0,0,.12)}
            .dot{width:56px;height:56px;border-radius:50%;background:{{color}};margin:0 auto 20px}
            h1{font-size:24px;font-weight:600;margin:0 0 12px}p{font-size:15px;line-height:1.5;margin:0;opacity:.8}
            </style></head><body><div class="card"><div class="dot"></div><h1>{{title}}</h1><p>{{text}}</p></div></body></html>
            """;
    }
}
