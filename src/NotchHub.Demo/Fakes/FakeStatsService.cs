using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;
using NotchHub.Demo.SampleData;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Demo system statistics (Ryzen 7 5800X ~37 %, RTX 3080 ~12 %, RAM 19.4 / 31.9 GB, disks, network, top apps with
/// generated icons). Samples every second while a live-updates token exists, otherwise every 5 s; frozen when
/// <see cref="DemoServices.LiveEvents"/> is false.
/// </summary>
public sealed class FakeStatsService : FakeBackgroundService, IStatsService
{
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    private readonly StatsSimulator _simulator = new();
    private readonly Dictionary<string, byte[]> _icons = [];
    private SystemStats _latest;
    private int _liveRequests;

    /// <summary>Creates the service with an initial snapshot (values exactly as in the reference screenshot).</summary>
    public FakeStatsService() => _latest = _simulator.Snapshot(DateTimeOffset.Now, _icons);

    /// <inheritdoc />
    public event EventHandler? Updated;

    /// <inheritdoc />
    public SystemStats? Latest
    {
        get { lock (Gate) return _latest; }
    }

    /// <inheritdoc />
    public IDisposable RequestLiveUpdates()
    {
        Interlocked.Increment(ref _liveRequests);
        return new LiveToken(this);
    }

    /// <inheritdoc />
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        var specs = ProcessCatalog.All.Select(p => p.Icon).ToArray();
        var icons = await Task.WhenAll(specs.Select(DemoAssets.IconAsync)).ConfigureAwait(false);
        lock (Gate)
        {
            for (var i = 0; i < specs.Length; i++)
                if (icons[i] is { } png) _icons[specs[i].CacheKey] = png;
            // Same values, now with icons (no jitter step: this is not a new sample).
            _latest = _simulator.Snapshot(_latest.Timestamp, _icons);
        }
        Raise(Updated);

        StartLiveLoop("stats", () => LiveInterval, () =>
        {
            lock (Gate)
            {
                var now = DateTimeOffset.Now;
                var interval = Volatile.Read(ref _liveRequests) > 0 ? LiveInterval : IdleInterval;
                // Small tolerance so 1 s ticks do not occasionally skip a live sample.
                if (now - _latest.Timestamp < interval - TimeSpan.FromMilliseconds(150)) return;
                _simulator.Step();
                _latest = _simulator.Snapshot(now, _icons);
            }
            Raise(Updated);
        }, cancellationToken);
    }

    private void Release() => Interlocked.Decrement(ref _liveRequests);

    /// <summary>Keeps fast sampling alive until disposed (idempotent).</summary>
    private sealed class LiveToken(FakeStatsService owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release();
        }
    }
}
