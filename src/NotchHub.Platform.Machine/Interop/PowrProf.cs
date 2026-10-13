using System.Runtime.InteropServices;

namespace NotchHub.Platform.Machine.Interop;

internal static unsafe partial class PowrProf
{
    /// <summary>sizeof(SYSTEM_POWER_CAPABILITIES) is 76 bytes; callers pass a larger zeroed buffer.</summary>
    public const int PowerCapabilitiesBufferSize = 128;

    // Byte offsets of the BOOLEAN members of SYSTEM_POWER_CAPABILITIES (winnt.h).
    public const int OffsetSystemS1 = 3;
    public const int OffsetSystemS2 = 4;
    public const int OffsetSystemS3 = 5;
    public const int OffsetSystemS4 = 6;
    public const int OffsetHiberFilePresent = 8;
    public const int OffsetAoAc = 20;

    /// <summary>BOOLEAN SetSuspendState(BOOLEAN bHibernate, BOOLEAN bForce, BOOLEAN bWakeupEventsDisabled). Blocks until resume.</summary>
    [LibraryImport("powrprof.dll", SetLastError = true)]
    public static partial byte SetSuspendState(byte hibernate, byte force, byte wakeupEventsDisabled);

    [LibraryImport("powrprof.dll")]
    public static partial byte GetPwrCapabilities(byte* capabilities);
}
