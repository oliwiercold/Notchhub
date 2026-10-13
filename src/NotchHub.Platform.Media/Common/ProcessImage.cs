using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace NotchHub.Platform.Media.Common;

/// <summary>
/// Read-only queries about other processes using only PROCESS_QUERY_LIMITED_INFORMATION, which is granted
/// even for elevated processes of the same user (unlike <see cref="Process.MainModule"/>).
/// </summary>
internal static unsafe class ProcessImage
{
    /// <summary>Full path of the process executable, or null when the process is gone or inaccessible.</summary>
    public static string? TryGetPath(uint processId)
    {
        if (processId == 0) return null;
        using var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        return handle.IsInvalid ? null : TryGetPath(handle);
    }

    /// <summary>Package-scoped AppUserModelID of a packaged (MSIX/UWP) process, or null for classic desktop apps.</summary>
    public static string? TryGetAppUserModelId(uint processId)
    {
        if (processId == 0) return null;
        using var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle.IsInvalid) return null;

        uint length = 0;
        var rc = Win32.GetApplicationUserModelId(handle, ref length, null);
        if (rc != Win32.ERROR_INSUFFICIENT_BUFFER || length == 0 || length > 1024) return null;
        var buffer = stackalloc char[(int)length];
        rc = Win32.GetApplicationUserModelId(handle, ref length, buffer);
        return rc == Win32.ERROR_SUCCESS ? new string(buffer, 0, Math.Max(0, (int)length - 1)) : null;
    }

    /// <summary>
    /// False only when the process definitely no longer exists; inaccessible processes count as alive.
    /// </summary>
    public static bool IsAlive(uint processId)
    {
        if (processId == 0) return true;
        using var handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle.IsInvalid)
            return System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != Win32.ERROR_INVALID_PARAMETER;
        return !Win32.GetExitCodeProcess(handle, out var code) || code == Win32.STILL_ACTIVE;
    }

    /// <summary>Distinct executable paths of running processes with the given image name (without ".exe").</summary>
    public static IReadOnlyList<string> FindPathsByName(string processName)
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName(processName); }
        catch (Exception) { return []; }

        var paths = new List<string>();
        foreach (var process in processes)
        {
            using (process)
            {
                var path = TryGetPath((uint)process.Id);
                if (path is not null && !paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
            }
        }
        return paths;
    }

    private static string? TryGetPath(SafeProcessHandle handle)
    {
        const int capacity = 1024;
        var buffer = stackalloc char[capacity];
        uint size = capacity;
        return Win32.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0
            ? new string(buffer, 0, (int)size)
            : null;
    }
}
