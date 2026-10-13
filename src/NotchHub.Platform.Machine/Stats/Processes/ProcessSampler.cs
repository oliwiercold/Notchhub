using System.Collections.Concurrent;
using System.Diagnostics;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Machine.Interop;

namespace NotchHub.Platform.Machine.Stats.Processes;

/// <summary>Process / thread totals from one NtQuerySystemInformation call.</summary>
internal readonly record struct ProcessTotals(int ProcessCount, int ThreadCount);

/// <summary>
/// Enumerates processes with a single <c>NtQuerySystemInformation(SystemProcessInformation)</c> call, groups them by
/// executable (like Task Manager "Apps") with private working set and CPU % (delta of kernel + user time), and resolves
/// friendly names (FileDescription) and 32 px icons lazily with caches. Not thread-safe: one sampling thread.
/// </summary>
internal sealed unsafe class ProcessSampler
{
    private const int TopCount = 8;
    private static readonly ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

    private readonly int _logicalProcessors;
    private readonly IconCache _icons = new();
    private readonly Dictionary<(nint Pid, long Created), long> _lastCpu = [];
    private readonly Dictionary<nint, string?> _paths = [];
    private byte[] _buffer = new byte[512 * 1024];
    private long _lastTimestamp;

    public ProcessSampler(int logicalProcessors) => _logicalProcessors = Math.Max(1, logicalProcessors);

    /// <summary>Counts only (cheap path when no live data is requested).</summary>
    public ProcessTotals CountOnly() => Walk(null);

    /// <summary>Counts + grouped top lists.</summary>
    public (ProcessTotals Totals, IReadOnlyList<ProcessGroupStats> ByMemory, IReadOnlyList<ProcessGroupStats> ByCpu) Sample()
    {
        var rows = new List<Row>(512);
        var totals = Walk(rows);

        var now = Stopwatch.GetTimestamp();
        var elapsed100ns = _lastTimestamp == 0 ? 0 : (now - _lastTimestamp) * 10_000_000.0 / Stopwatch.Frequency;
        _lastTimestamp = now;

        var seen = new HashSet<(nint, long)>();
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Pid == 0) continue; // System Idle Process
            var key = (row.Pid, row.Created);
            seen.Add(key);
            double cpu = 0;
            if (_lastCpu.TryGetValue(key, out var previous) && elapsed100ns > 0)
            {
                cpu = Math.Max(0, row.CpuTime - previous) / (elapsed100ns * _logicalProcessors) * 100.0;
            }
            _lastCpu[key] = row.CpuTime;

            if (!groups.TryGetValue(row.Name, out var group))
            {
                group = new Group(row.Name);
                groups[row.Name] = group;
            }
            group.MemoryBytes += row.PrivateBytes;
            group.Cpu += cpu;
            group.Count++;
            group.SamplePid ??= row.Pid;
        }
        foreach (var stale in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToArray()) _lastCpu.Remove(stale);
        var livePids = seen.Select(s => s.Item1).ToHashSet();
        foreach (var stalePid in _paths.Keys.Where(p => !livePids.Contains(p)).ToArray()) _paths.Remove(stalePid);

        var byMemory = groups.Values.OrderByDescending(g => g.MemoryBytes).Take(TopCount).ToArray();
        var byCpu = groups.Values.OrderByDescending(g => g.Cpu).ThenByDescending(g => g.MemoryBytes).Take(TopCount).ToArray();
        return (totals, byMemory.Select(ToStats).ToArray(), byCpu.Select(ToStats).ToArray());
    }

    private ProcessGroupStats ToStats(Group g)
    {
        var path = g.SamplePid is nint pid ? ResolvePath(pid) : null;
        var display = path is null ? Path.GetFileNameWithoutExtension(g.Key) : FriendlyName(path);
        return new ProcessGroupStats(g.Key.ToLowerInvariant(), display, g.MemoryBytes / (1024.0 * 1024.0),
            Math.Min(100, Math.Round(g.Cpu, 1)), g.Count, path is null ? null : _icons.Get(path));
    }

    private string? ResolvePath(nint pid)
    {
        if (_paths.TryGetValue(pid, out var cached)) return cached;
        string? path = null;
        using (var handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid))
        {
            if (!handle.IsInvalid)
            {
                char* buffer = stackalloc char[1024];
                uint size = 1024;
                if (Kernel32.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0) path = new string(buffer, 0, (int)size);
            }
        }
        _paths[pid] = path;
        return path;
    }

    private static string FriendlyName(string path)
    {
        if (Names.TryGetValue(path, out var name)) return name;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription.Trim()
                : !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName.Trim()
                : Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception)
        {
            name = Path.GetFileNameWithoutExtension(path);
        }
        if (Names.Count > 1024) Names.Clear();
        Names[path] = name;
        return name;
    }

    private ProcessTotals Walk(List<Row>? rows)
    {
        while (true)
        {
            fixed (byte* p = _buffer)
            {
                var status = NtDll.NtQuerySystemInformation(NtDll.SystemProcessInformation, p, (uint)_buffer.Length, out var needed);
                if (status == NtDll.STATUS_INFO_LENGTH_MISMATCH)
                {
                    // fall through to grow (outside the fixed block)
                    _ = needed;
                }
                else if (status < 0)
                {
                    return default;
                }
                else
                {
                    int processes = 0, threads = 0;
                    var offset = 0L;
                    while (true)
                    {
                        var info = (NtDll.SYSTEM_PROCESS_INFORMATION*)(p + offset);
                        processes++;
                        threads += (int)info->NumberOfThreads;
                        if (rows is not null)
                        {
                            var name = info->ImageName.Buffer is null || info->ImageName.Length == 0
                                ? (info->UniqueProcessId == 0 ? "Idle" : "System")
                                : new string(info->ImageName.Buffer, 0, info->ImageName.Length / 2);
                            rows.Add(new Row(info->UniqueProcessId, info->CreateTime, name, info->UserTime + info->KernelTime,
                                Math.Max(0, info->WorkingSetPrivateSize)));
                        }
                        if (info->NextEntryOffset == 0) break;
                        offset += info->NextEntryOffset;
                    }
                    return new ProcessTotals(processes - 1, threads); // minus the Idle pseudo process
                }
            }
            if (_buffer.Length >= 64 * 1024 * 1024) return default;
            _buffer = new byte[_buffer.Length * 2];
        }
    }

    private readonly record struct Row(nint Pid, long Created, string Name, long CpuTime, long PrivateBytes);

    private sealed class Group(string key)
    {
        public string Key { get; } = key;
        public long MemoryBytes { get; set; }
        public double Cpu { get; set; }
        public int Count { get; set; }
        public nint? SamplePid { get; set; }
    }
}
