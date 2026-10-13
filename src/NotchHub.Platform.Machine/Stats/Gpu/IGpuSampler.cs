using NotchHub.Core.Contracts;

namespace NotchHub.Platform.Machine.Stats.Gpu;

/// <summary>A GPU statistics backend (NVML or PDH/DXGI). Used from one sampling pass at a time.</summary>
internal interface IGpuSampler : IDisposable
{
    /// <summary>Backend name for logs.</summary>
    string Backend { get; }

    /// <summary>Reads current values; null when the reading failed (the caller decides whether to recreate the backend).</summary>
    GpuStats? Sample();
}

/// <summary>Which GPU backend to use. <see cref="Auto"/> prefers NVML and falls back to PDH.</summary>
internal enum GpuBackendPreference { Auto, Nvml, Pdh, None }
