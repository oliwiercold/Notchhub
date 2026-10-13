using System.Runtime.InteropServices;

namespace NotchHub.Platform.Machine.Interop;

internal static unsafe partial class Dxgi
{
    public static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    // Vtable slots (IUnknown 0-2, IDXGIObject 3-6, IDXGIFactory 7-11, IDXGIFactory1 12-13; IDXGIAdapter 7-9, IDXGIAdapter1 10).
    public const int SlotRelease = 2;
    public const int SlotFactoryEnumAdapters1 = 12;
    public const int SlotAdapterGetDesc1 = 10;

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(in Guid riid, out nint factory);

    public static void Release(nint comObject)
    {
        if (comObject == 0) return;
        var vtbl = *(nint**)comObject;
        ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl[SlotRelease])(comObject);
    }

    public static int EnumAdapters1(nint factory, uint index, out nint adapter)
    {
        var vtbl = *(nint**)factory;
        nint result;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)vtbl[SlotFactoryEnumAdapters1])(factory, index, &result);
        adapter = hr >= 0 ? result : 0;
        return hr;
    }

    public static int GetDesc1(nint adapter, out DXGI_ADAPTER_DESC1 desc)
    {
        var vtbl = *(nint**)adapter;
        DXGI_ADAPTER_DESC1 d;
        var hr = ((delegate* unmanaged[Stdcall]<nint, DXGI_ADAPTER_DESC1*, int>)vtbl[SlotAdapterGetDesc1])(adapter, &d);
        desc = d;
        return hr;
    }
}
