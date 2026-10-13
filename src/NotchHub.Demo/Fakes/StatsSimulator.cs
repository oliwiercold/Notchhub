using NotchHub.Core.Contracts;
using NotchHub.Demo.Imaging;
using NotchHub.Demo.Infrastructure;
using NotchHub.Demo.SampleData;

namespace NotchHub.Demo.Fakes;

/// <summary>
/// Produces plausible <see cref="SystemStats"/> for the demo machine (Ryzen 7 5800X, RTX 3080, 32 GB): every value is a
/// smooth mean-reverting random walk around the reference-screenshot value. Not thread-safe: the owner locks.
/// </summary>
internal sealed class StatsSimulator
{
    private const double Mib = 1024 * 1024;

    private readonly Random _random = DemoRandom.For("stats");
    private readonly DateTimeOffset _bootTime = DateTimeOffset.Now - new TimeSpan(5, 12, 0);

    private readonly Jitter _cpu = new(37, 18, 72, 2.6);
    private readonly Jitter _ghz = new(4.2, 3.9, 4.6, 0.05);
    private readonly Jitter _cpuTemp = new(54, 46, 68, 0.6);
    private readonly Jitter _processes = new(229, 221, 238, 0.8);
    private readonly Jitter _threads = new(3100, 3040, 3170, 9);
    private readonly Jitter _gpu = new(12, 4, 35, 1.6);
    private readonly Jitter _vram = new(2.1, 1.9, 2.4, 0.015);
    private readonly Jitter _gpuTemp = new(47, 40, 60, 0.4);
    private readonly Jitter _ram = new(19.4, 18.9, 20.2, 0.04);
    private readonly Jitter _down = new(1.2 * Mib, 0.15 * Mib, 6 * Mib, 0.22 * Mib, reversion: 0.35);
    private readonly Jitter _up = new(300 * 1024, 40 * 1024, 1.5 * Mib, 60 * 1024, reversion: 0.35);
    private readonly (DemoProcess Info, Jitter Memory, Jitter Cpu)[] _processGroups = ProcessCatalog.All
        .Select(p => (p, new Jitter(p.MemoryMb, p.MemoryMb * 0.9, p.MemoryMb * 1.15, p.MemoryMb * 0.006),
            new Jitter(p.CpuPercent, 0, p.CpuPercent * 3 + 1, Math.Max(0.1, p.CpuPercent * 0.12))))
        .ToArray();

    /// <summary>Advances every walk one step (one sampling interval).</summary>
    public void Step()
    {
        foreach (var jitter in new[] { _cpu, _ghz, _cpuTemp, _processes, _threads, _gpu, _vram, _gpuTemp, _ram, _down, _up })
            jitter.Next(_random);
        foreach (var (_, memory, cpu) in _processGroups)
        {
            memory.Next(_random);
            cpu.Next(_random);
        }
    }

    /// <summary>Builds a snapshot from the current values. <paramref name="icons"/> maps icon cache keys to PNGs.</summary>
    public SystemStats Snapshot(DateTimeOffset now, IReadOnlyDictionary<string, byte[]> icons)
    {
        var groups = _processGroups
            .Select(g => new ProcessGroupStats(g.Info.Key, g.Info.DisplayName, Math.Round(g.Memory.Value, 1), Math.Round(g.Cpu.Value, 1),
                g.Info.Instances, icons.GetValueOrDefault(g.Info.Icon.CacheKey)))
            .ToArray();

        const double totalRam = 31.9;
        var usedRam = Math.Round(_ram.Value, 2);
        return new SystemStats(
            now,
            new CpuStats("AMD Ryzen 7 5800X 8-Core Processor", Math.Round(_cpu.Value, 1), Math.Round(_ghz.Value, 2), 3.8, 8, 16,
                (int)Math.Round(_processes.Value), (int)Math.Round(_threads.Value), Math.Round(_cpuTemp.Value, 1)),
            new GpuStats("NVIDIA GeForce RTX 3080", Math.Round(_gpu.Value, 1), Math.Round(_vram.Value, 2), 10.0, Math.Round(_gpuTemp.Value, 1)),
            new MemoryStats(usedRam, totalRam, Math.Round(totalRam - usedRam, 2)),
            [
                new DiskStats(@"C:\", "Windows", 412.3, 931.5),
                new DiskStats(@"D:\", "Data", 1229.4, 1863.0),
            ],
            new NetworkThroughput(Math.Round(_down.Value), Math.Round(_up.Value)),
            now - _bootTime,
            groups.OrderByDescending(g => g.MemoryMb).Take(ProcessCatalog.TopCount).ToArray(),
            groups.OrderByDescending(g => g.CpuPercent).Take(ProcessCatalog.TopCount).ToArray());
    }
}
