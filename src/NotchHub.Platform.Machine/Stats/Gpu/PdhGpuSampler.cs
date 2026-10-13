using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Machine.Stats.Counters;

namespace NotchHub.Platform.Machine.Stats.Gpu;

/// <summary>
/// Vendor-neutral GPU statistics like Task Manager: per-engine utilization summed over processes from
/// "GPU Engine(*)\Utilization Percentage" (the busiest engine wins — usually 3D) and "GPU Adapter Memory(*)\Dedicated Usage",
/// both filtered to the DXGI adapter with the most dedicated memory. Temperature is not available here.
/// </summary>
internal sealed class PdhGpuSampler : IGpuSampler
{
    private const double BytesPerGb = 1024.0 * 1024 * 1024;

    private readonly DxgiAdapterInfo _adapter;
    private readonly PdhQuery _query;
    private readonly PdhCounter _engines;
    private readonly PdhCounter? _dedicated;
    private readonly string _luidToken;
    private readonly Dictionary<int, double> _engineTotals = new();
    private readonly PdhInstanceVisitor _engineVisitor;
    private readonly PdhInstanceVisitor _memoryVisitor;
    private double _dedicatedBytes;

    private PdhGpuSampler(DxgiAdapterInfo adapter, PdhQuery query, PdhCounter engines, PdhCounter? dedicated)
    {
        _adapter = adapter;
        _query = query;
        _engines = engines;
        _dedicated = dedicated;
        _luidToken = adapter.PdhLuidToken;
        _engineVisitor = VisitEngine;
        _memoryVisitor = VisitMemory;
    }

    public string Backend => "PDH";

    public static PdhGpuSampler? TryCreate()
    {
        var adapter = DxgiAdapters.FindPrimary();
        if (adapter is null) return null;
        var query = PdhQuery.TryOpen();
        if (query is null) return null;
        var engines = query.TryAddCounter(@"\GPU Engine(*)\Utilization Percentage");
        if (engines is null)
        {
            query.Dispose();
            return null;
        }
        var dedicated = query.TryAddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");
        query.Collect(); // utilization is a rate counter: prime it
        Log.Info($"Stats: PDH GPU backend for '{adapter.Description}' ({adapter.PdhLuidToken})");
        return new PdhGpuSampler(adapter, query, engines, dedicated);
    }

    public GpuStats? Sample()
    {
        if (!_query.Collect()) return null;

        _engineTotals.Clear();
        _engines.ForEachInstance(_engineVisitor);
        var usage = _engineTotals.Count == 0 ? 0 : _engineTotals.Values.Max();

        _dedicatedBytes = 0;
        _dedicated?.ForEachInstance(_memoryVisitor);

        return new GpuStats(
            _adapter.Description,
            Math.Clamp(usage, 0, 100),
            _dedicatedBytes / BytesPerGb,
            _adapter.DedicatedVideoMemory / BytesPerGb,
            null);
    }

    // Instance: "pid_1234_luid_0x00000000_0x0000E222_phys_0_eng_3_engtype_3D"
    private void VisitEngine(ReadOnlySpan<char> instance, double value)
    {
        if (value <= 0 || instance.IndexOf(_luidToken, StringComparison.OrdinalIgnoreCase) < 0) return;
        var key = EngineKey(instance);
        if (key < 0) return;
        _engineTotals[key] = _engineTotals.GetValueOrDefault(key) + value;
    }

    // Instance: "luid_0x00000000_0x0000E222_phys_0"
    private void VisitMemory(ReadOnlySpan<char> instance, double value)
    {
        if (instance.StartsWith(_luidToken, StringComparison.OrdinalIgnoreCase)) _dedicatedBytes += value;
    }

    /// <summary>phys * 1024 + eng, or -1 when the instance name has an unexpected shape.</summary>
    internal static int EngineKey(ReadOnlySpan<char> instance)
    {
        var phys = ReadNumberAfter(instance, "_phys_");
        var eng = ReadNumberAfter(instance, "_eng_");
        return phys < 0 || eng < 0 ? -1 : phys * 1024 + eng;
    }

    private static int ReadNumberAfter(ReadOnlySpan<char> s, string marker)
    {
        var i = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return -1;
        var rest = s[(i + marker.Length)..];
        var end = 0;
        while (end < rest.Length && char.IsAsciiDigit(rest[end])) end++;
        return end > 0 && int.TryParse(rest[..end], out var n) ? n : -1;
    }

    public void Dispose() => _query.Dispose();
}
