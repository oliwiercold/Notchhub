// Read-only diagnostics for NotchHub.Platform.Machine. PowerService is only queried (IsSupported), never executed.
// Pass "speed" to run one speed test.
using System.IO;
using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Core.Settings;
using NotchHub.Platform.Machine.Power;
using NotchHub.Platform.Machine.SpeedTest;
using NotchHub.Platform.Machine.Stats;

var root = Path.Combine(Path.GetTempPath(), "NotchHubHarnessMachine");
AppPaths.Initialize(root);
var settings = new SettingsService(Path.Combine(root, "settings.json"));

using var stats = new StatsService(settings);
await stats.StartAsync();
await Task.Delay(1200);
var idle = stats.Latest;
Console.WriteLine($"[idle] cpu={idle?.Cpu.UsagePercent}% procs={idle?.Cpu.ProcessCount} topByMem={idle?.TopByMemory.Count}");
using (stats.RequestLiveUpdates())
{
    await Task.Delay(2600);
}
var s = stats.Latest!;
Console.WriteLine($"[cpu] {s.Cpu}");
Console.WriteLine($"[gpu] {s.Gpu}");
Console.WriteLine($"[mem] {s.Memory.UsedGb:0.0}/{s.Memory.TotalGb:0.0} GB ({s.Memory.UsagePercent:0}%)");
foreach (var d in s.Disks) Console.WriteLine($"[disk] {d.Name} {d.Label} {d.UsedGb:0}/{d.TotalGb:0} GB");
Console.WriteLine($"[net] down={s.Network.DownBytesPerSec / 1024:0.0} KB/s up={s.Network.UpBytesPerSec / 1024:0.0} KB/s uptime={s.Uptime}");
foreach (var p in s.TopByMemory) Console.WriteLine($"  mem {p.DisplayName,-30} {p.MemoryMb,8:0} MB cpu={p.CpuPercent,5:0.0}% x{p.InstanceCount} icon={(p.IconPng is { Length: > 8 } png && png[1] == 'P' ? png.Length + "B" : "-")}");
foreach (var p in s.TopByCpu.Take(4)) Console.WriteLine($"  cpu {p.DisplayName,-30} {p.CpuPercent,5:0.0}%");

var power = new PowerService();
Console.WriteLine("[power] " + string.Join(", ", Enum.GetValues<PowerAction>().Select(a => $"{a}={power.IsSupported(a)}")));

if (args.Contains("speed"))
{
    using var speed = new SpeedTestService();
    await speed.StartAsync();
    var lastPhase = SpeedTestPhase.Idle;
    speed.Changed += (_, _) =>
    {
        if (speed.Phase != lastPhase) { lastPhase = speed.Phase; Console.WriteLine($"  phase {speed.Phase} progress={speed.Progress:0.00}"); }
    };
    await speed.RunAsync();
    Console.WriteLine($"[speed] {speed.Phase} {speed.LastResult} err={speed.LastError} saved={File.Exists(Path.Combine(root, "speedtest.json"))}");
}
