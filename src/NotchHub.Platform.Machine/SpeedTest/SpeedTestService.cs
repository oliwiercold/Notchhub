using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Machine.SpeedTest;

/// <summary>
/// Speed test against Cloudflare (speed.cloudflare.com): latency (median of small requests, jitter = mean absolute
/// difference of consecutive samples), then download and upload with parallel streams for a fixed duration.
/// Live Mbps is a sliding one-second window; the result excludes the TCP ramp-up of the first second.
/// The last result is persisted to <c>speedtest.json</c>.
/// </summary>
public sealed class SpeedTestService : ISpeedTestService
{
    private const string BaseUrl = "https://speed.cloudflare.com";
    private const int Streams = 4;
    private const int LatencySamples = 12;
    private const long DownloadChunk = 25_000_000;
    private const int UploadChunk = 4_000_000;
    private static readonly TimeSpan PhaseDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LatencyRequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LatencyPhaseTimeout = TimeSpan.FromSeconds(20);

    private static readonly HttpClient Http = CreateClient();
    private static readonly byte[] UploadPayload = CreatePayload();

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private SpeedTestPhase _phase = SpeedTestPhase.Idle;
    private double _progress;
    private double _currentMbps;
    private SpeedTestResult? _last;
    private string? _lastError;
    private long _lastRaise;
    private string? _headerLocation;

    public SpeedTestPhase Phase => _phase;
    public bool IsRunning => _phase is SpeedTestPhase.Latency or SpeedTestPhase.Download or SpeedTestPhase.Upload;
    public double Progress => Volatile.Read(ref _progress);
    public double CurrentMbps => Volatile.Read(ref _currentMbps);
    public SpeedTestResult? LastResult => Volatile.Read(ref _last);
    public string? LastError => _lastError;

    public event EventHandler? Changed;

    private static string StorePath => AppPaths.File("speedtest.json");

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Streams * 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        // Per-request deadlines come from the phase cancellation tokens.
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NotchHub/1.0");
        return client;
    }

    private static byte[] CreatePayload()
    {
        var bytes = new byte[UploadChunk];
        Random.Shared.NextBytes(bytes); // incompressible
        return bytes;
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var stored = JsonStore.Load<SpeedTestResult?>(StorePath, () => null);
        if (stored is not null)
        {
            Volatile.Write(ref _last, stored);
            Raise(force: true);
        }
    }, cancellationToken);

    public async Task RunAsync(CancellationToken ct = default)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (IsRunning) return;
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            _lastError = null;
            SetPhase(SpeedTestPhase.Latency, 0);
        }

        try
        {
            var token = cts.Token;
            var locationTask = GetLocationAsync(token);
            var (latency, jitter) = await MeasureLatencyAsync(token).ConfigureAwait(false);

            SetPhase(SpeedTestPhase.Download, 0.1);
            var down = await MeasureAsync(DownloadStreamAsync, 0.1, 0.55, token).ConfigureAwait(false);

            SetPhase(SpeedTestPhase.Upload, 0.55);
            var up = await MeasureAsync(UploadStreamAsync, 0.55, 1.0, token).ConfigureAwait(false);

            var location = await locationTask.ConfigureAwait(false) ?? _headerLocation;
            var result = new SpeedTestResult(DateTimeOffset.Now, Math.Round(down, 1), Math.Round(up, 1), Math.Round(latency, 1),
                jitter is double j ? Math.Round(j, 1) : null, location);
            Volatile.Write(ref _last, result);
            try { JsonStore.Save(StorePath, result); }
            catch (Exception ex) { Log.Warn("SpeedTest: saving the result failed", ex); }
            Log.Info($"SpeedTest: {result.DownloadMbps} / {result.UploadMbps} Mbps, {result.LatencyMs} ms");
            Volatile.Write(ref _currentMbps, 0);
            SetPhase(SpeedTestPhase.Completed, 1);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _lastError = null;
            Volatile.Write(ref _currentMbps, 0);
            SetPhase(SpeedTestPhase.Idle, 0);
        }
        catch (Exception ex)
        {
            Log.Warn("SpeedTest: failed", ex);
            _lastError = ex is HttpRequestException hre ? $"HTTP error{(hre.StatusCode is { } code ? $" {(int)code}" : "")}: {hre.Message}" : ex.Message;
            Volatile.Write(ref _currentMbps, 0);
            SetPhase(SpeedTestPhase.Failed, 0);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_cts, cts)) _cts = null;
            }
            cts.Dispose();
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    // ───────────────────────────── phases ─────────────────────────────

    private async Task<(double Median, double? Jitter)> MeasureLatencyAsync(CancellationToken ct)
    {
        var samples = new List<double>();
        // Each request and the whole phase get their own deadline (the HttpClient has no global timeout), so a reply
        // blackholed by a dropped link / VPN / captive portal cannot keep the test in the Latency phase forever.
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phase.CancelAfter(LatencyPhaseTimeout);
        for (var i = 0; i < LatencySamples; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(phase.Token);
            request.CancelAfter(LatencyRequestTimeout);
            var sw = Stopwatch.StartNew();
            HttpResponseMessage resp;
            try
            {
                resp = await Http.GetAsync($"{BaseUrl}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Latency request timed out", ex); // reported as Failed, not as a user cancel
            }
            using (var response = resp)
            {
                response.EnsureSuccessStatusCode();
                sw.Stop();
                // Cloudflare reports its own processing time; subtract it when present.
                var serverMs = ServerTiming(response);
                if (i == 0) _headerLocation = HeaderLocation(response);
                if (i > 0) samples.Add(Math.Max(0.1, sw.Elapsed.TotalMilliseconds - serverMs)); // first request includes TLS setup
            }
            SetProgress(0.1 * (i + 1) / LatencySamples);
        }
        // Jitter: mean absolute difference of consecutive samples (chronological order).
        double? jitter = samples.Count < 2 ? null : samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        var sorted = samples.Order().ToArray();
        var median = sorted.Length == 0 ? 0 : sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        return (median, jitter);
    }

    /// <summary>__down responses carry cf-meta-* headers (used when /meta is unavailable).</summary>
    private static string? HeaderLocation(HttpResponseMessage response)
    {
        string? Get(string name) => response.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
        var city = Get("cf-meta-city");
        var colo = Get("cf-meta-colo");
        if (string.IsNullOrEmpty(colo) && Get("cf-ray") is { } ray && ray.LastIndexOf('-') is var dash and > 0) colo = ray[(dash + 1)..]; // "8c1f...-WAW"
        if (string.IsNullOrEmpty(colo)) return city;
        return string.IsNullOrEmpty(city) ? colo : $"{city} · {colo}";
    }

    private static double ServerTiming(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Server-Timing", out var values)) return 0;
        foreach (var v in values)
        {
            var i = v.IndexOf("dur=", StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var span = v.AsSpan(i + 4);
            var end = span.IndexOfAny(';', ',');
            if (end >= 0) span = span[..end];
            if (double.TryParse(span, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms)) return ms;
        }
        return 0;
    }

    /// <summary>Runs <see cref="Streams"/> parallel workers for <see cref="PhaseDuration"/>; returns Mbps after warm-up.</summary>
    private async Task<double> MeasureAsync(Func<Counter, CancellationToken, Task> worker, double progressFrom, double progressTo, CancellationToken ct)
    {
        var counter = new Counter();
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phase.CancelAfter(PhaseDuration);
        var workers = Enumerable.Range(0, Streams).Select(_ => RunWorker(worker, counter, phase.Token)).ToArray();

        var sw = Stopwatch.StartNew();
        var window = new Queue<(double T, long Bytes)>();
        long warmupBytes = -1;
        double warmupAt = 0;
        while (!phase.IsCancellationRequested)
        {
            try { await Task.Delay(200, phase.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            var t = sw.Elapsed.TotalSeconds;
            var bytes = Interlocked.Read(ref counter.Bytes);
            window.Enqueue((t, bytes));
            while (window.Count > 1 && t - window.Peek().T > 1.0) window.Dequeue();
            var (t0, b0) = window.Peek();
            if (t > t0) Volatile.Write(ref _currentMbps, (bytes - b0) * 8 / (t - t0) / 1_000_000);
            if (warmupBytes < 0 && sw.Elapsed >= Warmup)
            {
                warmupBytes = bytes;
                warmupAt = t;
            }
            SetProgress(progressFrom + (progressTo - progressFrom) * Math.Min(1, sw.Elapsed / PhaseDuration));
            if (workers.All(w => w.IsCompleted)) break;
        }
        await Task.WhenAll(workers).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (counter.Error is not null && Interlocked.Read(ref counter.Bytes) == 0) throw counter.Error;

        var total = sw.Elapsed.TotalSeconds;
        var totalBytes = Interlocked.Read(ref counter.Bytes);
        if (warmupBytes < 0 || total - warmupAt < 0.5) return total > 0 ? totalBytes * 8 / total / 1_000_000 : 0;
        return (totalBytes - warmupBytes) * 8 / (total - warmupAt) / 1_000_000;
    }

    private static async Task RunWorker(Func<Counter, CancellationToken, Task> worker, Counter counter, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await worker(counter, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                counter.Error ??= ex;
                if (ct.IsCancellationRequested) return;
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private static async Task DownloadStreamAsync(Counter counter, CancellationToken ct)
    {
        using var response = await Http.GetAsync($"{BaseUrl}/__down?bytes={DownloadChunk}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            Interlocked.Add(ref counter.Bytes, read);
        }
    }

    private static async Task UploadStreamAsync(Counter counter, CancellationToken ct)
    {
        using var content = new CountingContent(UploadPayload, counter);
        using var response = await Http.PostAsync($"{BaseUrl}/__up", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> GetLocationAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var stream = await Http.GetStreamAsync($"{BaseUrl}/meta", timeout.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            var root = doc.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var city = Get("city");
            var colo = Get("colo");
            var country = Get("country");
            if (!string.IsNullOrEmpty(colo)) return string.IsNullOrEmpty(city) ? $"{colo} ({country})" : $"{city} · {colo}";
            return city ?? country;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Info($"SpeedTest: /meta unavailable ({ex.GetType().Name}), using response headers");
            return null;
        }
    }

    // ───────────────────────────── state ─────────────────────────────

    private void SetPhase(SpeedTestPhase phase, double progress)
    {
        _phase = phase;
        Volatile.Write(ref _progress, progress);
        Raise(force: true);
    }

    private void SetProgress(double progress)
    {
        Volatile.Write(ref _progress, Math.Clamp(progress, 0, 1));
        Raise(force: false);
    }

    /// <summary>Raises <see cref="Changed"/>; live updates are throttled to ~5 per second.</summary>
    private void Raise(bool force)
    {
        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastRaise) < 180) return;
        Interlocked.Exchange(ref _lastRaise, now);
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("SpeedTest: Changed handler failed", ex); }
    }

    public void Dispose() => Cancel();

    private sealed class Counter
    {
        public long Bytes;
        public Exception? Error;
    }

    /// <summary>Request body that reports bytes as they are written to the socket.</summary>
    private sealed class CountingContent : HttpContent
    {
        private readonly byte[] _payload;
        private readonly Counter _counter;

        public CountingContent(byte[] payload, Counter counter)
        {
            _payload = payload;
            _counter = counter;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            const int chunk = 64 * 1024;
            for (var offset = 0; offset < _payload.Length; offset += chunk)
            {
                var count = Math.Min(chunk, _payload.Length - offset);
                await stream.WriteAsync(_payload.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref _counter.Bytes, count);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _payload.Length;
            return true;
        }
    }
}
