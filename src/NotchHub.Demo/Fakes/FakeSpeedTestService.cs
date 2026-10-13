using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Demo.Infrastructure;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo internet speed test. The last result is 85 ↓ / 42 ↑ Mbps, 14 ms (Warsaw) from five minutes ago; a run simulates
/// latency → download → upload over about 7 s with live throughput. No network traffic is generated.
/// </summary>
public sealed class FakeSpeedTestService : FakeBackgroundService, ISpeedTestService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan LatencyDuration = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan TransferDuration = TimeSpan.FromSeconds(2.9);
    private const string Server = "Warsaw";

    private readonly Random _random = DemoRandom.For("speedtest");
    private SpeedTestPhase _phase = SpeedTestPhase.Idle;
    private double _progress;
    private double _currentMbps;
    private string? _lastError;
    private SpeedTestResult? _lastResult = new(DateTimeOffset.Now.AddMinutes(-5), 85, 42, 14, 1.8, Server);
    private CancellationTokenSource? _run;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public SpeedTestPhase Phase
    {
        get { lock (Gate) return _phase; }
    }

    /// <inheritdoc />
    public bool IsRunning
    {
        get { lock (Gate) return _run is not null; }
    }

    /// <inheritdoc />
    public double Progress
    {
        get { lock (Gate) return _progress; }
    }

    /// <inheritdoc />
    public double CurrentMbps
    {
        get { lock (Gate) return _currentMbps; }
    }

    /// <inheritdoc />
    public SpeedTestResult? LastResult
    {
        get { lock (Gate) return _lastResult; }
    }

    /// <inheritdoc />
    public string? LastError
    {
        get { lock (Gate) return _lastError; }
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken ct = default)
    {
        CancellationTokenSource run;
        lock (Gate)
        {
            if (_run is not null || IsDisposed) return;
            run = _run = CancellationTokenSource.CreateLinkedTokenSource(ct, Lifetime);
            _lastError = null;
        }

        try
        {
            var latency = await MeasureLatencyAsync(run.Token).ConfigureAwait(false);
            var down = await TransferAsync(SpeedTestPhase.Download, 85, 0.15, 0.55, run.Token).ConfigureAwait(false);
            var up = await TransferAsync(SpeedTestPhase.Upload, 42, 0.55, 1.0, run.Token).ConfigureAwait(false);
            lock (Gate)
            {
                _lastResult = new SpeedTestResult(DateTimeOffset.Now, Math.Round(down, 1), Math.Round(up, 1), Math.Round(latency), 1.4 + _random.NextDouble(), Server);
                _phase = SpeedTestPhase.Completed;
                _progress = 1;
                _currentMbps = 0;
            }
            Log.Info("DEMO: speed test completed");
        }
        catch (OperationCanceledException)
        {
            lock (Gate)
            {
                _phase = SpeedTestPhase.Idle;
                _progress = 0;
                _currentMbps = 0;
            }
        }
        finally
        {
            lock (Gate) _run = null;
            run.Dispose();
        }
        Raise(Changed);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        CancellationTokenSource? run;
        lock (Gate) run = _run;
        // Outside the lock: cancellation may run the rest of RunAsync (and its event handlers) inline.
        try { run?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <inheritdoc />
    protected override void OnDispose() => Cancel();

    private async Task<double> MeasureLatencyAsync(CancellationToken ct)
    {
        Update(SpeedTestPhase.Latency, 0, 0);
        var steps = (int)(LatencyDuration / Tick);
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(Tick, ct).ConfigureAwait(false);
            Update(SpeedTestPhase.Latency, 0.15 * i / steps, 0);
        }
        lock (Gate) return 13 + _random.NextDouble() * 3;
    }

    /// <summary>Ramps up to around <paramref name="targetMbps"/> (TCP slow start) with noise; returns the average of the plateau.</summary>
    private async Task<double> TransferAsync(SpeedTestPhase phase, double targetMbps, double progressFrom, double progressTo, CancellationToken ct)
    {
        Update(phase, progressFrom, 0);
        var steps = (int)(TransferDuration / Tick);
        var plateau = new List<double>();
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(Tick, ct).ConfigureAwait(false);
            var t = i / (double)steps;
            double noise;
            lock (Gate) noise = Jitter.Gaussian(_random);
            var mbps = Math.Max(0.1, targetMbps * (1 - Math.Exp(-t * 6)) + noise * targetMbps * 0.04);
            if (t > 0.4) plateau.Add(mbps);
            Update(phase, progressFrom + (progressTo - progressFrom) * t, mbps);
        }
        return plateau.Count > 0 ? plateau.Average() : targetMbps;
    }

    private void Update(SpeedTestPhase phase, double progress, double mbps)
    {
        lock (Gate)
        {
            _phase = phase;
            _progress = progress;
            _currentMbps = Math.Round(mbps, 1);
        }
        Raise(Changed);
    }
}
