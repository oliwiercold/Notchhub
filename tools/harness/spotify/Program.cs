// Offline checks of the Spotify helpers (no real sign-in, no network except a 127.0.0.1 loopback self-test).
using System.Net;
using System.Net.Sockets;
using System.Text;
using NotchHub.Platform.Media.Common;
using NotchHub.Platform.Media.Media.Spotify;

int failed = 0, passed = 0;
void Check(string name, bool ok)
{
    if (ok) passed++; else failed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
}

// ── PKCE ──
// RFC 7636 appendix B test vector
Check("challenge RFC7636 vector", SpotifyAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
var v1 = SpotifyAuth.CreateVerifier();
var v2 = SpotifyAuth.CreateVerifier();
Check("verifier length 64", v1.Length == 64);
Check("verifier charset", v1.All(c => char.IsAsciiLetterOrDigit(c) || "-._~".Contains(c)));
Check("verifier random", v1 != v2);
Check("verifier bounds", Throws(() => SpotifyAuth.CreateVerifier(42)) && Throws(() => SpotifyAuth.CreateVerifier(129)));
var state = SpotifyAuth.CreateState();
Check("state url-safe", state.Length >= 20 && !state.Contains('=') && !state.Contains('+') && !state.Contains('/'));
var url = SpotifyAuth.AuthorizeUrl("abc123", "CHAL", "ST");
Check("authorize url", url.StartsWith("https://accounts.spotify.com/authorize?response_type=code&client_id=abc123")
    && url.Contains("code_challenge_method=S256") && url.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A43819%2Fcallback")
    && url.Contains("user-library-modify") && url.Contains("&state=ST"));
Check("redirect uri", SpotifyAuth.RedirectUri == "http://127.0.0.1:43819/callback");
var form = SpotifyAuth.CodeExchangeForm("id", "CODE", "VER");
Check("code form", form["grant_type"] == "authorization_code" && form["code_verifier"] == "VER" && form["redirect_uri"] == SpotifyAuth.RedirectUri && !form.ContainsKey("client_secret"));

// ── callback parsing ──
var cb = SpotifyAuth.ParseCallback("/callback?code=AQB%2Bx_y&state=s%3D1");
Check("callback code/state", cb is { Code: "AQB+x_y", State: "s=1", Error: null });
Check("callback error", SpotifyAuth.ParseCallback("/callback?error=access_denied&state=x") is { Error: "access_denied", Code: null });
Check("callback other path", SpotifyAuth.ParseCallback("/favicon.ico") is null && SpotifyAuth.ParseCallback("/callbackx?code=1") is null);
Check("request line", SpotifyAuth.RequestTarget("GET /callback?code=1 HTTP/1.1") == "/callback?code=1" && SpotifyAuth.RequestTarget("POST / HTTP/1.1") is null);

// ── JSON ──
var tok = SpotifyJson.ParseToken("""{"access_token":"AT","token_type":"Bearer","scope":"user-library-read","expires_in":3600,"refresh_token":"RT"}""");
Check("token", tok is { AccessToken: "AT", RefreshToken: "RT", ExpiresIn: 3600 });
Check("token without refresh", SpotifyJson.ParseToken("""{"access_token":"AT2","expires_in":10}""").RefreshToken is null);
Check("token missing access", Throws(() => SpotifyJson.ParseToken("""{"error":"x"}""")));
Check("oauth error", SpotifyJson.ParseOAuthError("""{"error":"invalid_grant","error_description":"Refresh token revoked"}""") == "invalid_grant");
Check("oauth error junk", SpotifyJson.ParseOAuthError("<html>") is null && SpotifyJson.ParseOAuthError(null) is null);
Check("api reason", SpotifyJson.ParseApiReason("""{"error":{"status":403,"message":"Player command failed: Premium required","reason":"PREMIUM_REQUIRED"}}""") == "PREMIUM_REQUIRED");
Check("api reason absent", SpotifyJson.ParseApiReason("""{"error":{"status":401,"message":"The access token expired"}}""") is null);
Check("profile", SpotifyJson.ParseProfileName("""{"display_name":"Oliwier","id":"u1"}""") == "Oliwier"
    && SpotifyJson.ParseProfileName("""{"display_name":null,"id":"u1"}""") == "u1");
var pb = SpotifyJson.ParsePlayback("""
{"device":{"id":"d","name":"PC"},"shuffle_state":true,"smart_shuffle":false,"repeat_state":"context","timestamp":1,"progress_ms":1000,
 "is_playing":true,"currently_playing_type":"track",
 "item":{"type":"track","name":"Blinding Lights","uri":"spotify:track:0VjIjW4GlUZAMYd2vXMi3b","id":"0VjIjW4GlUZAMYd2vXMi3b",
   "artists":[{"name":"The Weeknd"},{"name":"Guest"}]}}
""");
Check("playback", pb is { ItemUri: "spotify:track:0VjIjW4GlUZAMYd2vXMi3b", ItemName: "Blinding Lights", Artists: "The Weeknd, Guest",
    IsPlaying: true, Shuffle: true, SmartShuffle: false, Repeat: "context" });
Check("playback 204", SpotifyJson.ParsePlayback("") is null);
var ad = SpotifyJson.ParsePlayback("""{"is_playing":true,"shuffle_state":false,"repeat_state":"off","item":null,"currently_playing_type":"ad"}""");
Check("playback ad (no item)", ad is { ItemUri: null, ItemName: "", Shuffle: false });
var ep = SpotifyJson.ParsePlayback("""{"is_playing":false,"item":{"type":"episode","name":"Ep 1","uri":"spotify:episode:1","show":{"name":"Pod"}}}""");
Check("playback episode", ep is { ItemUri: "spotify:episode:1", Artists: "Pod", Shuffle: null, Repeat: null });
Check("contains", SpotifyJson.ParseContains("[true,false]") is [true, false]);
Check("contains not array", Throws(() => SpotifyJson.ParseContains("""{"a":1}""")));

// ── DPAPI ──
var secret = Encoding.UTF8.GetBytes("{\"refreshToken\":\"x\"}");
var entropy = "e"u8.ToArray();
var sealedBytes = Dpapi.Protect(secret, entropy);
Check("dpapi roundtrip", Dpapi.Unprotect(sealedBytes, entropy).AsSpan().SequenceEqual(secret) && !sealedBytes.AsSpan().SequenceEqual(secret));
Check("dpapi wrong entropy", Throws(() => Dpapi.Unprotect(sealedBytes, "f"u8.ToArray())));

// ── loopback callback (ephemeral port on 127.0.0.1; ignores /favicon, returns on /callback) ──
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
{
    var wait = SpotifyClient.WaitForCallbackAsync(listener, cts.Token);
    using var http = new HttpClient();
    var fav = await http.GetAsync($"http://127.0.0.1:{port}/favicon.ico");
    var ok = await http.GetAsync($"http://127.0.0.1:{port}/callback?code=C0DE&state=ST8");
    var body = await ok.Content.ReadAsStringAsync();
    var got = await wait;
    Check("loopback 404 for favicon", (int)fav.StatusCode == 404);
    Check("loopback callback", got is { Code: "C0DE", State: "ST8" } && (int)ok.StatusCode == 200 && body.Contains("Możesz zamknąć"));
}
listener.Stop();

// ── loopback robustness: an idle preconnect socket and a reset socket must not block or abort the real redirect ──
listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
port = ((IPEndPoint)listener.LocalEndpoint).Port;
using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
{
    var wait = SpotifyClient.WaitForCallbackAsync(listener, cts.Token);
    using var idle = new TcpClient();
    await idle.ConnectAsync(IPAddress.Loopback, port); // never sends anything (Chromium preconnect)
    using (var reset = new TcpClient())
    {
        await reset.ConnectAsync(IPAddress.Loopback, port);
        reset.Client.LingerState = new LingerOption(true, 0); // close with RST
        await reset.GetStream().WriteAsync("GE"u8.ToArray());
    }
    await Task.Delay(100);
    using var http = new HttpClient();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var ok = await http.GetAsync($"http://127.0.0.1:{port}/callback?code=C2&state=S2");
    SpotifyAuth.Callback? got = null;
    Exception? err = null;
    try { got = await wait; } catch (Exception ex) { err = ex; }
    Check("loopback idle+reset sockets do not block callback", err is null && got is { Code: "C2", State: "S2" } && (int)ok.StatusCode == 200 && sw.Elapsed < TimeSpan.FromSeconds(5));
}
using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
{
    bool cancelled;
    try { await SpotifyClient.WaitForCallbackAsync(listener, cts.Token); cancelled = false; }
    catch (OperationCanceledException) { cancelled = true; }
    Check("loopback honours cancellation", cancelled);
}
listener.Stop();

// ── refresh vs sign-out / re-sign-in races (generation guard) ──
var store = Path.Combine(Path.GetTempPath(), $"nh-spotify-harness-{Environment.ProcessId}.bin");
var entropyField = typeof(SpotifyClient).GetField("Entropy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var storeEntropy = (byte[])entropyField.GetValue(null)!;
File.WriteAllBytes(store, Dpapi.Protect(Encoding.UTF8.GetBytes("""{"clientId":"cid","refreshToken":"RT1","account":"A"}"""), storeEntropy));
using (var client = new SpotifyClient(store))
{
    client.Load("cid");
    Check("session loaded", client.HasSession && client.Account == "A");
    var gen = client.SessionGeneration;
    Check("current refresh applies", client.TryApplyRefresh(gen, new SpotifyToken("AT", "RT2", 3600, null), out var rotated) && rotated);
    var stale = client.SessionGeneration;
    client.SignOut();
    Check("stale refresh after sign-out discarded", !client.TryApplyRefresh(stale, new SpotifyToken("AT3", "RT3", 3600, null), out _)
        && !client.HasSession && !File.Exists(store));
    File.WriteAllBytes(store, Dpapi.Protect(Encoding.UTF8.GetBytes("""{"clientId":"cid","refreshToken":"NEW","account":"B"}"""), storeEntropy));
    var beforeReload = client.SessionGeneration;
    client.Load("cid");
    Check("stale refresh after reload discarded", client.HasSession && !client.TryApplyRefresh(beforeReload, new SpotifyToken("AT4", "OLD", 3600, null), out _));
}
try { File.Delete(store); } catch { }

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static bool Throws(Action a)
{
    try { a(); return false; }
    catch { return true; }
}
