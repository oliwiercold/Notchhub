using NotchHub.Platform.Machine.Interop;
using NotchHub.Platform.Machine.Stats.Counters;

namespace NotchHub.Platform.Machine.Stats.Cpu;

/// <summary>One CPU reading: Task Manager style utility and the effective clock relative to base (100 = base).</summary>
internal readonly record struct CpuSample(double UsagePercent, double? PerformancePercent);

/// <summary>
/// CPU usage like Task Manager: PDH "% Processor Utility" (frequency-aware) and "% Processor Performance"
/// (effective clock). Falls back to GetSystemTimes deltas when the performance counters are unavailable.
/// </summary>
internal sealed class CpuSampler : IDisposable
{
    private readonly PdhQuery? _query;
    private readonly PdhCounter? _utility;
    private readonly PdhCounter? _performance;
    private long _lastIdle, _lastKernel, _lastUser;
    private bool _hasSystemTimes;

    public CpuSampler()
    {
        _query = PdhQuery.TryOpen();
        if (_query is not null)
        {
            _utility = _query.TryAddCounter(@"\Processor Information(_Total)\% Processor Utility");
            _performance = _query.TryAddCounter(@"\Processor Information(_Total)\% Processor Performance");
            if (_utility is null && _performance is null)
            {
                _query.Dispose();
                _query = null;
            }
            else
            {
                _query.Collect(); // rate counters need a first raw sample
            }
        }
        ReadSystemTimesUsage(); // establish the fallback baseline
    }

    public CpuSample Sample()
    {
        var fallback = ReadSystemTimesUsage();
        double? utility = null, performance = null;
        if (_query?.Collect() == true)
        {
            utility = _utility?.GetDouble();
            performance = _performance?.GetDouble();
        }
        var usage = utility ?? fallback ?? 0;
        return new CpuSample(Math.Clamp(usage, 0, 100), performance is > 0 ? performance : null);
    }

    private double? ReadSystemTimesUsage()
    {
        if (!Kernel32.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        double? usage = null;
        if (_hasSystemTimes)
        {
            // Kernel time includes idle time.
            var total = (kernel - _lastKernel) + (user - _lastUser);
            var idleDelta = idle - _lastIdle;
            if (total > 0) usage = (1.0 - (double)idleDelta / total) * 100.0;
        }
        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;
        _hasSystemTimes = true;
        return usage;
    }

    public void Dispose() => _query?.Dispose();
}
