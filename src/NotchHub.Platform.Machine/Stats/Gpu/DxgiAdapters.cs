using NotchHub.Core;
using NotchHub.Platform.Machine.Interop;

namespace NotchHub.Platform.Machine.Stats.Gpu;

/// <summary>A hardware display adapter reported by DXGI.</summary>
internal sealed record DxgiAdapterInfo(string Description, uint VendorId, ulong DedicatedVideoMemory, uint LuidLow, int LuidHigh)
{
    /// <summary>LUID as it appears in PDH GPU instance names, e.g. "luid_0x00000000_0x0000E222".</summary>
    public string PdhLuidToken => $"luid_0x{LuidHigh:X8}_0x{LuidLow:X8}";
}

/// <summary>Enumerates adapters through IDXGIFactory1 (raw vtable calls, no extra dependencies).</summary>
internal static class DxgiAdapters
{
    private const uint MicrosoftVendorId = 0x1414; // Basic Render Driver / WARP

    public static unsafe IReadOnlyList<DxgiAdapterInfo> EnumerateHardware()
    {
        var result = new List<DxgiAdapterInfo>();
        nint factory = 0;
        try
        {
            if (Dxgi.CreateDXGIFactory1(Dxgi.IID_IDXGIFactory1, out factory) < 0 || factory == 0) return result;
            for (uint i = 0; i < 16; i++)
            {
                var hr = Dxgi.EnumAdapters1(factory, i, out var adapter);
                if (hr == Dxgi.DXGI_ERROR_NOT_FOUND || hr < 0) break;
                try
                {
                    if (Dxgi.GetDesc1(adapter, out var desc) < 0) continue;
                    if ((desc.Flags & Dxgi.DXGI_ADAPTER_FLAG_SOFTWARE) != 0 || desc.VendorId == MicrosoftVendorId) continue;
                    var name = new string(desc.Description).TrimEnd('\0').Trim();
                    result.Add(new DxgiAdapterInfo(name, desc.VendorId, desc.DedicatedVideoMemory, desc.AdapterLuidLow, desc.AdapterLuidHigh));
                }
                finally
                {
                    Dxgi.Release(adapter);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("Stats: DXGI unavailable", ex);
        }
        finally
        {
            Dxgi.Release(factory);
        }
        return result;
    }

    /// <summary>The adapter with the most dedicated memory (the discrete GPU on hybrid systems).</summary>
    public static DxgiAdapterInfo? FindPrimary() =>
        EnumerateHardware().OrderByDescending(a => a.DedicatedVideoMemory).FirstOrDefault();
}
