using System.Runtime.InteropServices;
using NotchHub.Core;

namespace NotchHub.Platform.Machine.Stats.Gpu;

/// <summary>
/// Dynamically loaded NVIDIA Management Library (nvml.dll ships with the driver in System32).
/// Loaded by absolute path only, never through the DLL search order.
/// </summary>
internal sealed unsafe class NvmlLibrary : IDisposable
{
    public const int Success = 0;
    public const int TemperatureGpu = 0;
    public const int DeviceNameBufferSize = 96;

    [StructLayout(LayoutKind.Sequential)]
    public struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryV1
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    /// <summary>nvmlMemory_v2_t: "used" excludes the driver-reserved part, matching Task Manager's dedicated usage.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryV2
    {
        public uint Version;
        public ulong Total;
        public ulong Reserved;
        public ulong Free;
        public ulong Used;
    }

    public static readonly uint MemoryV2Version = (uint)sizeof(MemoryV2) | (2u << 24);

    private nint _module;
    private bool _initialized;

    private delegate* unmanaged[Cdecl]<int> _init;
    private delegate* unmanaged[Cdecl]<int> _shutdown;
    private delegate* unmanaged[Cdecl]<uint*, int> _deviceGetCount;
    private delegate* unmanaged[Cdecl]<uint, nint*, int> _deviceGetHandleByIndex;
    private delegate* unmanaged[Cdecl]<nint, byte*, uint, int> _deviceGetName;
    private delegate* unmanaged[Cdecl]<nint, Utilization*, int> _deviceGetUtilizationRates;
    private delegate* unmanaged[Cdecl]<nint, MemoryV1*, int> _deviceGetMemoryInfo;
    private delegate* unmanaged[Cdecl]<nint, MemoryV2*, int> _deviceGetMemoryInfoV2;
    private delegate* unmanaged[Cdecl]<nint, int, uint*, int> _deviceGetTemperature;

    private NvmlLibrary(nint module) => _module = module;

    /// <summary>Loads and initializes NVML; null when the library or an NVIDIA driver is not present.</summary>
    public static NvmlLibrary? TryLoad()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var module)) continue;
            var lib = new NvmlLibrary(module);
            if (lib.Bind() && lib.Initialize()) return lib;
            lib.Dispose();
        }
        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(Environment.SystemDirectory, "nvml.dll");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll");
    }

    private bool Bind()
    {
        nint Get(string name) => NativeLibrary.TryGetExport(_module, name, out var p) ? p : 0;

        var init = Get("nvmlInit_v2");
        var shutdown = Get("nvmlShutdown");
        var count = Get("nvmlDeviceGetCount_v2");
        var byIndex = Get("nvmlDeviceGetHandleByIndex_v2");
        var name = Get("nvmlDeviceGetName");
        var util = Get("nvmlDeviceGetUtilizationRates");
        var mem = Get("nvmlDeviceGetMemoryInfo");
        if (init == 0 || shutdown == 0 || count == 0 || byIndex == 0 || name == 0 || util == 0 || mem == 0)
        {
            Log.Warn("Stats: nvml.dll lacks required exports");
            return false;
        }
        _init = (delegate* unmanaged[Cdecl]<int>)init;
        _shutdown = (delegate* unmanaged[Cdecl]<int>)shutdown;
        _deviceGetCount = (delegate* unmanaged[Cdecl]<uint*, int>)count;
        _deviceGetHandleByIndex = (delegate* unmanaged[Cdecl]<uint, nint*, int>)byIndex;
        _deviceGetName = (delegate* unmanaged[Cdecl]<nint, byte*, uint, int>)name;
        _deviceGetUtilizationRates = (delegate* unmanaged[Cdecl]<nint, Utilization*, int>)util;
        _deviceGetMemoryInfo = (delegate* unmanaged[Cdecl]<nint, MemoryV1*, int>)mem;
        _deviceGetMemoryInfoV2 = (delegate* unmanaged[Cdecl]<nint, MemoryV2*, int>)Get("nvmlDeviceGetMemoryInfo_v2");
        _deviceGetTemperature = (delegate* unmanaged[Cdecl]<nint, int, uint*, int>)Get("nvmlDeviceGetTemperature");
        return true;
    }

    private bool Initialize()
    {
        var rc = _init();
        if (rc != Success)
        {
            Log.Info($"Stats: nvmlInit failed ({rc}), NVIDIA statistics unavailable");
            return false;
        }
        _initialized = true;
        return true;
    }

    public int GetDeviceCount(out uint count)
    {
        uint c = 0;
        var rc = _deviceGetCount(&c);
        count = c;
        return rc;
    }

    public int GetHandleByIndex(uint index, out nint device)
    {
        nint d = 0;
        var rc = _deviceGetHandleByIndex(index, &d);
        device = d;
        return rc;
    }

    public string? GetName(nint device)
    {
        var buffer = stackalloc byte[DeviceNameBufferSize];
        if (_deviceGetName(device, buffer, DeviceNameBufferSize) != Success) return null;
        return Marshal.PtrToStringUTF8((nint)buffer)?.Trim();
    }

    public int GetUtilization(nint device, out Utilization utilization)
    {
        Utilization u;
        var rc = _deviceGetUtilizationRates(device, &u);
        utilization = u;
        return rc;
    }

    /// <summary>Returns (used, total) bytes, preferring the v2 API where "used" excludes reserved memory.</summary>
    public bool TryGetMemory(nint device, out ulong used, out ulong total)
    {
        if (_deviceGetMemoryInfoV2 != null)
        {
            var v2 = new MemoryV2 { Version = MemoryV2Version };
            if (_deviceGetMemoryInfoV2(device, &v2) == Success)
            {
                used = v2.Used;
                total = v2.Total;
                return true;
            }
        }
        MemoryV1 v1;
        if (_deviceGetMemoryInfo(device, &v1) == Success)
        {
            used = v1.Used;
            total = v1.Total;
            return true;
        }
        used = total = 0;
        return false;
    }

    public double? GetTemperature(nint device)
    {
        if (_deviceGetTemperature == null) return null;
        uint t;
        return _deviceGetTemperature(device, TemperatureGpu, &t) == Success && t is > 0 and < 150 ? t : null;
    }

    public void Dispose()
    {
        if (_initialized)
        {
            _initialized = false;
            try { _shutdown(); } catch (Exception ex) { Log.Warn("Stats: nvmlShutdown failed", ex); }
        }
        var module = Interlocked.Exchange(ref _module, 0);
        if (module != 0) NativeLibrary.Free(module);
    }
}
