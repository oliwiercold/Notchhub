using System.Runtime.InteropServices;

namespace NotchHub.Platform.Machine.Interop;

internal static unsafe partial class NtDll
{
    public const int SystemProcessInformation = 5;
    public const int SystemProcessIdInformation = 88;
    public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int systemInformationClass, void* systemInformation, uint systemInformationLength, out uint returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    /// <summary>
    /// Leading part of SYSTEM_PROCESS_INFORMATION (winternl.h + the documented-by-practice Vista+ fields).
    /// Only read through a pointer into the NtQuerySystemInformation buffer; the trailing members are not declared.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_PROCESS_INFORMATION
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UNICODE_STRING ImageName;
        public int BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
        public uint HandleCount;
        public uint SessionId;
        public nuint UniqueProcessKey;
        public nuint PeakVirtualSize;
        public nuint VirtualSize;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
    }

    /// <summary>Input/output of SystemProcessIdInformation: the caller supplies the name buffer.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_PROCESS_ID_INFORMATION
    {
        public nint ProcessId;
        public UNICODE_STRING ImageName;
    }
}
