using NotchHub.Core;
using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Machine.Stats.Gpu;

/// <summary>NVIDIA GPU statistics through NVML (utilization, VRAM, temperature) for the device with the most memory.</summary>
internal sealed class NvmlGpuSampler : IGpuSampler
{
    private const double BytesPerGb = 1024.0 * 1024 * 1024;

    private readonly NvmlLibrary _nvml;
    private readonly nint _device;
    private readonly string _name;

    private NvmlGpuSampler(NvmlLibrary nvml, nint device, string name)
    {
        _nvml = nvml;
        _device = device;
        _name = name;
    }

    public string Backend => "NVML";

    public static NvmlGpuSampler? TryCreate()
    {
        NvmlLibrary? nvml = null;
        try
        {
            nvml = NvmlLibrary.TryLoad();
            if (nvml is null) return null;
            if (nvml.GetDeviceCount(out var count) != NvmlLibrary.Success || count == 0)
            {
                nvml.Dispose();
                return null;
            }

            nint best = 0;
            ulong bestTotal = 0;
            for (uint i = 0; i < count; i++)
            {
                if (nvml.GetHandleByIndex(i, out var device) != NvmlLibrary.Success) continue;
                nvml.TryGetMemory(device, out _, out var total);
                if (best == 0 || total > bestTotal)
                {
                    best = device;
                    bestTotal = total;
                }
            }
            if (best == 0)
            {
                nvml.Dispose();
                return null;
            }

            var name = nvml.GetName(best);
            return new NvmlGpuSampler(nvml, best, string.IsNullOrWhiteSpace(name) ? "NVIDIA GPU" : name);
        }
        catch (Exception ex)
        {
            Log.Warn("Stats: NVML initialization failed", ex);
            nvml?.Dispose();
            return null;
        }
    }

    public GpuStats? Sample()
    {
        if (_nvml.GetUtilization(_device, out var util) != NvmlLibrary.Success) return null;
        if (!_nvml.TryGetMemory(_device, out var used, out var total)) return null;
        return new GpuStats(
            _name,
            Math.Clamp(util.Gpu, 0, 100),
            used / BytesPerGb,
            total / BytesPerGb,
            _nvml.GetTemperature(_device));
    }

    public void Dispose() => _nvml.Dispose();
}
