using System.Net;
using System.Text.Json;

namespace NotchHub.Services.Common;

/// <summary>HTTP plumbing shared by the network-backed services (each service still owns its own client).</summary>
internal static class ServiceHttp
{
    public const string UserAgent = "NotchHub/1.0";

    /// <summary>Creates a client with the NotchHub User-Agent, the given overall timeout and DNS-friendly pooling.</summary>
    public static HttpClient Create(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            // Recycle pooled connections so DNS changes (Wi-Fi roaming, VPN) are picked up.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// Maps a failure of an HTTP round trip to a stable, non-localized error code:
    /// "http_&lt;status&gt;", "timeout", "network", "parse" or "unknown".
    /// </summary>
    public static string ErrorCode(Exception ex, CancellationToken callerToken) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => $"http_{(int)status}",
        HttpRequestException => "network",
        TimeoutException => "timeout",
        // HttpClient.Timeout surfaces as TaskCanceledException without the caller having cancelled.
        OperationCanceledException when !callerToken.IsCancellationRequested => "timeout",
        JsonException or FormatException => "parse",
        IOException => "network",
        _ => "unknown",
    };

    /// <summary>True for failures that are expected in normal operation (offline, server hiccups) — logged without stack traces.</summary>
    public static bool IsExpectedNetworkFailure(Exception ex) =>
        ex is HttpRequestException or TimeoutException or OperationCanceledException or IOException;
}
