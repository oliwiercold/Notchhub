using System.Diagnostics;
using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Machine.Stats.Gpu;

/// <summary>
/// Owns the active GPU backend: creates it lazily, smooths over transient read failures and recreates it
/// after repeated failures (driver update / TDR), with back-off so a missing GPU costs nothing.
/// </summary>
internal sealed class GpuMonitor : IDisposable
{
    private static readonly TimeSpan RetryAfterMissing = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);
    private const int MaxConsecutiveFailures = 5;

    private readonly GpuBackendPreference _preference;
    private IGpuSampler? _sampler;
    private GpuStats? _last;
    private int _failures;
    private long _retryAt;

    public GpuMonitor(GpuBackendPreference preference) => _preference = preference;

    /// <summary>Name of the active backend ("NVML", "PDH") or null.</summary>
    public string? Backend => _sampler?.Backend;

    public GpuStats? Sample()
    {
        if (_preference == GpuBackendPreference.None) return null;
        if (_sampler is null)
        {
            if (Stopwatch.GetTimestamp() < _retryAt) return null;
            _sampler = CreateSampler();
            if (_sampler is null)
            {
                _retryAt = Stopwatch.GetTimestamp() + (long)(RetryAfterMissing.TotalSeconds * Stopwatch.Frequency);
                return null;
            }
        }

        GpuStats? stats = null;
        try { stats = _sampler.Sample(); }
        catch (Exception ex) { Log.Warn($"Stats: GPU sample failed ({_sampler.Backend})", ex); }

        if (stats is not null)
        {
            _failures = 0;
            _last = stats;
            return stats;
        }

        if (++_failures < MaxConsecutiveFailures) return _last;

        Log.Warn($"Stats: GPU backend {_sampler.Backend} keeps failing, recreating later");
        DisposeSampler();
        _last = null;
        _failures = 0;
        _retryAt = Stopwatch.GetTimestamp() + (long)(RetryAfterFailure.TotalSeconds * Stopwatch.Frequency);
        return null;
    }

    private IGpuSampler? CreateSampler()
    {
        try
        {
            IGpuSampler? sampler = null;
            if (_preference is GpuBackendPreference.Auto or GpuBackendPreference.Nvml)
                sampler = NvmlGpuSampler.TryCreate();
            if (sampler is null && _preference is GpuBackendPreference.Auto or GpuBackendPreference.Pdh)
                sampler = PdhGpuSampler.TryCreate();
            if (sampler is not null) Log.Info($"Stats: GPU backend {sampler.Backend} active");
            else Log.Info("Stats: no GPU statistics backend available");
            return sampler;
        }
        catch (Exception ex)
        {
            Log.Warn("Stats: GPU backend creation failed", ex);
            return null;
        }
    }

    private void DisposeSampler()
    {
        var s = _sampler;
        _sampler = null;
        Log.Safe("Stats: GPU backend dispose", () => s?.Dispose());
    }

    public void Dispose() => DisposeSampler();
}
