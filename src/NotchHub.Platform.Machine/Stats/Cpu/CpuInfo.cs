using Microsoft.Win32;
using NotchHub.Core;
using NotchHub.Platform.Machine.Interop;

namespace NotchHub.Platform.Machine.Stats.Cpu;

/// <summary>Static processor description (read once).</summary>
internal sealed record CpuInfo(string Name, double BaseMhz, int Cores, int LogicalProcessors)
{
    public double BaseGhz => BaseMhz / 1000.0;

    public static CpuInfo Query()
    {
        var name = "CPU";
        double baseMhz = 0;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string s && !string.IsNullOrWhiteSpace(s))
                name = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (key?.GetValue("~MHz") is int mhz && mhz > 0)
                baseMhz = mhz;
        }
        catch (Exception ex)
        {
            Log.Warn("Stats: cannot read processor description from the registry", ex);
        }

        var logical = 0;
        try { logical = (int)Kernel32.GetActiveProcessorCount(Kernel32.ALL_PROCESSOR_GROUPS); }
        catch (Exception ex) { Log.Warn("Stats: GetActiveProcessorCount failed", ex); }
        if (logical <= 0) logical = Environment.ProcessorCount;

        var cores = CountPhysicalCores();
        return new CpuInfo(name, baseMhz, cores > 0 ? cores : logical, logical);
    }

    private static unsafe int CountPhysicalCores()
    {
        try
        {
            uint length = 0;
            Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationProcessorCore, null, ref length);
            if (length == 0) return 0;
            var buffer = new byte[length];
            fixed (byte* p = buffer)
            {
                if (!Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationProcessorCore, p, ref length)) return 0;
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: { int Relationship; uint Size; ... } variable-sized records.
                int count = 0, offset = 0;
                while (offset + 8 <= length)
                {
                    var size = *(uint*)(p + offset + 4);
                    if (size == 0) break;
                    if (*(int*)(p + offset) == Kernel32.RelationProcessorCore) count++;
                    offset += (int)size;
                }
                return count;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Stats: cannot count physical cores", ex);
            return 0;
        }
    }
}
