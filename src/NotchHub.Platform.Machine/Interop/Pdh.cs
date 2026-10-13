using System.Runtime.InteropServices;

namespace NotchHub.Platform.Machine.Interop;

internal static unsafe partial class Pdh
{
    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_FMT_NOCAP100 = 0x00008000;
    public const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    public const uint PDH_CSTATUS_VALID_DATA = 0;
    public const uint PDH_CSTATUS_NEW_DATA = 1;

    [StructLayout(LayoutKind.Explicit)]
    public struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
        [FieldOffset(8)] public long largeValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM_W
    {
        public char* szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PdhOpenQuery(string? dataSource, nint userData, out nint query);

    /// <summary>Adds a counter by its English (non-localized) path, so it works on any Windows display language.</summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PdhAddEnglishCounter(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    public static partial int PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    public static partial int PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial int PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, void* itemBuffer);

    [LibraryImport("pdh.dll")]
    public static partial int PdhCloseQuery(nint query);
}
