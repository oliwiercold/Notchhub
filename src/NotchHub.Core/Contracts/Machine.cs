namespace NotchHub.Core.Contracts;

// ───────────────────────────── Stats ─────────────────────────────

/// <param name="CurrentGhz">Effective clock (like Task Manager "Speed"); null if unknown.</param>
public sealed record CpuStats(string Name, double UsagePercent, double? CurrentGhz, double BaseGhz, int Cores, int LogicalProcessors, int ProcessCount, int ThreadCount, double? TemperatureC);

public sealed record GpuStats(string Name, double UsagePercent, double VramUsedGb, double VramTotalGb, double? TemperatureC);

public sealed record MemoryStats(double UsedGb, double TotalGb, double AvailableGb)
{
    public double UsagePercent => TotalGb <= 0 ? 0 : UsedGb / TotalGb * 100.0;
}

/// <param name="Name">Drive root, e.g. "C:\".</param>
public sealed record DiskStats(string Name, string? Label, double UsedGb, double TotalGb);

public sealed record NetworkThroughput(double DownBytesPerSec, double UpBytesPerSec);

/// <summary>Processes grouped by executable (like Task Manager "Apps").</summary>
/// <param name="Key">Lower-case executable name, e.g. "chrome.exe".</param>
/// <param name="DisplayName">File description, e.g. "Google Chrome".</param>
/// <param name="IconPng">32x32 PNG of the executable icon (cached by the service).</param>
public sealed record ProcessGroupStats(string Key, string DisplayName, double MemoryMb, double CpuPercent, int InstanceCount, byte[]? IconPng);

public sealed record SystemStats(
    DateTimeOffset Timestamp,
    CpuStats Cpu,
    GpuStats? Gpu,
    MemoryStats Memory,
    IReadOnlyList<DiskStats> Disks,
    NetworkThroughput Network,
    TimeSpan Uptime,
    IReadOnlyList<ProcessGroupStats> TopByMemory,
    IReadOnlyList<ProcessGroupStats> TopByCpu);

public interface IStatsService : IBackgroundService
{
    SystemStats? Latest { get; }
    event EventHandler? Updated;
    /// <summary>
    /// While at least one returned token is alive, sample every ~1 s (Stats tab visible).
    /// Otherwise sample slowly (~10 s, without per-process data) or not at all. Dispose the token to release.
    /// </summary>
    IDisposable RequestLiveUpdates();
}

// ───────────────────────────── Speed test ─────────────────────────────

public enum SpeedTestPhase { Idle, Latency, Download, Upload, Completed, Failed }

public sealed record SpeedTestResult(DateTimeOffset At, double DownloadMbps, double UploadMbps, double LatencyMs, double? JitterMs, string? ServerLocation);

public interface ISpeedTestService : IBackgroundService
{
    SpeedTestPhase Phase { get; }
    bool IsRunning { get; }
    /// <summary>0..1 overall progress of the running test.</summary>
    double Progress { get; }
    /// <summary>Live throughput of the current phase in Mbps.</summary>
    double CurrentMbps { get; }
    /// <summary>Last completed result (persisted across restarts).</summary>
    SpeedTestResult? LastResult { get; }
    string? LastError { get; }
    event EventHandler? Changed;
    /// <summary>Runs latency, download and upload tests. No-op if already running.</summary>
    Task RunAsync(CancellationToken ct = default);
    void Cancel();
}

// ───────────────────────────── Power ─────────────────────────────

public enum PowerAction { Lock, Sleep, Hibernate, SignOut, Restart, Shutdown }

public interface IPowerService
{
    bool IsSupported(PowerAction action);
    Task<bool> ExecuteAsync(PowerAction action);
}
