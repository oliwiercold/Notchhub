using System.Diagnostics;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Platform.Machine.Interop;
using NotchHub.Platform.Machine.Stats.Cpu;
using NotchHub.Platform.Machine.Stats.Gpu;
using NotchHub.Platform.Machine.Stats.Processes;

namespace NotchHub.Platform.Machine.Stats;

/// <summary>
/// System statistics. Samples every 5 s without per-process data in the background, and every 1 s with grouped top
/// processes while at least one <see cref="RequestLiveUpdates"/> token is alive. All sampling runs on one timer
/// callback at a time (never overlapping).
/// </summary>
public sealed class StatsService(ISettingsService settings) : IStatsService
{
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    private readonly ISettingsService _settings = settings;
    private readonly object _gate = new();
    private Timer? _timer;
    private CpuInfo? _cpuInfo;
    private CpuSampler? _cpu;
    private GpuMonitor? _gpu;
    private ProcessSampler? _processes;
    private SystemStats? _latest;
    private int _liveTokens;
    private int _sampling;
    private ulong _lastIn, _lastOut;
    private long _lastNetTimestamp;
    private bool _disposed;

    public SystemStats? Latest => Volatile.Read(ref _latest);

    public event EventHandler? Updated;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            _cpuInfo = CpuInfo.Query();
            _cpu = new CpuSampler();
            _gpu = new GpuMonitor(GpuBackendPreference.Auto);
            _processes = new ProcessSampler(_cpuInfo.LogicalProcessors);
            ReadNetwork(out _lastIn, out _lastOut);
            _lastNetTimestamp = Stopwatch.GetTimestamp();
        }, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed) return;
            // First sample shortly after start so rate counters have a baseline.
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(500), CurrentInterval());
        }
        Log.Info($"Stats: started (CPU '{_cpuInfo?.Name}', {_cpuInfo?.Cores}C/{_cpuInfo?.LogicalProcessors}T)");
    }

    private TimeSpan CurrentInterval() => Volatile.Read(ref _liveTokens) > 0 ? LiveInterval : IdleInterval;

    public IDisposable RequestLiveUpdates()
    {
        if (Interlocked.Increment(ref _liveTokens) == 1)
        {
            lock (_gate) _timer?.Change(TimeSpan.Zero, LiveInterval); // fresh data with processes right away
        }
        return new LiveToken(this);
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _liveTokens) == 0)
        {
            lock (_gate) _timer?.Change(IdleInterval, IdleInterval);
        }
    }

    private void Tick()
    {
        if (_disposed || Interlocked.Exchange(ref _sampling, 1) == 1) return;
        try
        {
            var stats = Sample(Volatile.Read(ref _liveTokens) > 0);
            if (stats is null) return;
            Volatile.Write(ref _latest, stats);
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error("Stats: sampling failed", ex);
        }
        finally
        {
            Volatile.Write(ref _sampling, 0);
        }
    }

    private SystemStats? Sample(bool withProcesses)
    {
        var info = _cpuInfo;
        if (info is null || _cpu is null || _processes is null) return null;

        var cpuSample = _cpu.Sample();
        double? ghz = cpuSample.PerformancePercent is double perf && info.BaseMhz > 0
            ? Math.Round(info.BaseGhz * perf / 100.0, 2)
            : null;

        ProcessTotals totals;
        IReadOnlyList<ProcessGroupStats> byMemory = [], byCpu = [];
        if (withProcesses)
        {
            (totals, byMemory, byCpu) = _processes.Sample();
        }
        else
        {
            totals = _processes.CountOnly();
        }

        var cpu = new CpuStats(info.Name, Math.Round(cpuSample.UsagePercent, 1), ghz, Math.Round(info.BaseGhz, 2), info.Cores,
            info.LogicalProcessors, totals.ProcessCount, totals.ThreadCount, null);

        GpuStats? gpu = null;
        try { gpu = _gpu?.Sample(); }
        catch (Exception ex) { Log.Warn("Stats: GPU sample failed", ex); }

        return new SystemStats(DateTimeOffset.Now, cpu, gpu, ReadMemory(), ReadDisks(), ReadThroughput(),
            TimeSpan.FromMilliseconds(Environment.TickCount64), byMemory, byCpu);
    }

    private static MemoryStats ReadMemory()
    {
        var status = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Kernel32.MEMORYSTATUSEX>() };
        if (!Kernel32.GlobalMemoryStatusEx(ref status)) return new MemoryStats(0, 0, 0);
        const double gb = 1024.0 * 1024 * 1024;
        var total = status.ullTotalPhys / gb;
        var available = status.ullAvailPhys / gb;
        return new MemoryStats(total - available, total, available);
    }

    private IReadOnlyList<DiskStats> ReadDisks()
    {
        const double gb = 1024.0 * 1024 * 1024;
        var preferred = _settings.Current.Stats.Disk;
        var list = new List<DiskStats>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    var total = drive.TotalSize / gb;
                    list.Add(new DiskStats(drive.Name, string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel,
                        total - drive.TotalFreeSpace / gb, total));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Stats: drive enumeration failed", ex);
        }
        // The configured drive first.
        return list.OrderByDescending(d => string.Equals(d.Name, preferred, StringComparison.OrdinalIgnoreCase)).ThenBy(d => d.Name).ToArray();
    }

    private NetworkThroughput ReadThroughput()
    {
        if (!ReadNetwork(out var inOctets, out var outOctets)) return new NetworkThroughput(0, 0);
        var now = Stopwatch.GetTimestamp();
        var seconds = (now - _lastNetTimestamp) / (double)Stopwatch.Frequency;
        var down = seconds > 0 && inOctets >= _lastIn ? (inOctets - _lastIn) / seconds : 0;
        var up = seconds > 0 && outOctets >= _lastOut ? (outOctets - _lastOut) / seconds : 0;
        _lastIn = inOctets;
        _lastOut = outOctets;
        _lastNetTimestamp = now;
        return new NetworkThroughput(down, up);
    }

    /// <summary>Sums octet counters of physical, connected, non-filter interfaces.</summary>
    private static unsafe bool ReadNetwork(out ulong inOctets, out ulong outOctets)
    {
        inOctets = outOctets = 0;
        if (IpHlpApi.GetIfTable2(out var table) != 0 || table == 0) return false;
        try
        {
            var header = (IpHlpApi.MIB_IF_TABLE2*)table;
            var rows = &header->Table;
            for (var i = 0; i < header->NumEntries; i++)
            {
                var row = &rows[i];
                if (row->Type == IpHlpApi.IF_TYPE_SOFTWARE_LOOPBACK || row->OperStatus != IpHlpApi.IfOperStatusUp) continue;
                var flags = row->InterfaceAndOperStatusFlags;
                if ((flags & IpHlpApi.FlagHardwareInterface) == 0 || (flags & IpHlpApi.FlagFilterInterface) != 0) continue;
                inOctets += row->InOctets;
                outOctets += row->OutOctets;
            }
            return true;
        }
        finally
        {
            IpHlpApi.FreeMibTable(table);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
        // Let an in-flight sample finish before releasing native resources.
        SpinWait.SpinUntil(() => Volatile.Read(ref _sampling) == 0, 2000);
        _cpu?.Dispose();
        _gpu?.Dispose();
    }

    private sealed class LiveToken(StatsService owner) : IDisposable
    {
        private StatsService? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
