using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NotchHub.Platform.Shell.Internal.Interop;

namespace NotchHub.Platform.Shell.Internal;

/// <summary>Executable path + friendly name of a process.</summary>
internal sealed record AppIdentity(string? ExePath, string ExeName, string FriendlyName);

/// <summary>
/// Resolves windows to the app that owns them: process image path, executable name and a friendly name
/// (file description). Results are cached per executable path; safe to call from any thread.
/// </summary>
internal static unsafe class ProcessInfo
{
    private const int MaxCacheEntries = 512;
    private static readonly ConcurrentDictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase);

    public static readonly int CurrentProcessId = Environment.ProcessId;

    /// <summary>
    /// Process id of the app behind <paramref name="hwnd"/>. UWP windows are hosted by ApplicationFrameHost.exe;
    /// for those the hosted CoreWindow's process is returned.
    /// </summary>
    public static uint GetAppProcessId(nint hwnd)
    {
        var pid = Win32.GetWindowProcessId(hwnd);
        if (pid == 0) return 0;
        var path = Win32.GetProcessImagePath(pid);
        if (path is not null && Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var hosted = FindHostedProcess(hwnd, pid);
            if (hosted != 0) return hosted;
        }
        return pid;
    }

    /// <summary>Identity of the process, or null when it cannot be queried (exited / protected).</summary>
    public static AppIdentity? Describe(uint processId)
    {
        if (processId == 0) return null;
        var path = Win32.GetProcessImagePath(processId);
        if (path is null)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var name = process.ProcessName + ".exe";
                return new AppIdentity(null, name, process.ProcessName);
            }
            catch (Exception)
            {
                return null; // exited or inaccessible
            }
        }

        return new AppIdentity(path, Path.GetFileName(path), GetFriendlyName(path));
    }

    /// <summary>Identity of the app owning <paramref name="hwnd"/>.</summary>
    public static AppIdentity? DescribeWindow(nint hwnd) => hwnd == 0 ? null : Describe(GetAppProcessId(hwnd));

    /// <summary>File description of the executable (e.g. "Google Chrome"), falling back to product / file name.</summary>
    public static string GetFriendlyName(string exePath)
    {
        if (FriendlyNames.TryGetValue(exePath, out var cached)) return cached;

        string name;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            name = FirstNonEmpty(info.FileDescription, info.ProductName) ?? Path.GetFileNameWithoutExtension(exePath);
        }
        catch (Exception)
        {
            name = Path.GetFileNameWithoutExtension(exePath);
        }

        if (FriendlyNames.Count > MaxCacheEntries) FriendlyNames.Clear();
        FriendlyNames[exePath] = name;
        return name;
    }

    /// <summary>True when <paramref name="exeName"/> matches an entry like "KeePassXC.exe" or "keepassxc".</summary>
    public static bool MatchesExe(string exeName, IEnumerable<string> patterns)
    {
        var bare = Path.GetFileNameWithoutExtension(exeName);
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var p = pattern.Trim();
            if (p.Equals(exeName, StringComparison.OrdinalIgnoreCase)) return true;
            if (Path.GetFileNameWithoutExtension(p).Equals(bare, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed)) return trimmed;
        }
        return null;
    }

    private static uint FindHostedProcess(nint frame, uint framePid)
    {
        var state = new HostedSearch(framePid);
        var handle = GCHandle.Alloc(state);
        try
        {
            Win32.EnumChildWindows(frame, &EnumChildCallback, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return state.Found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int EnumChildCallback(nint hwnd, nint lParam)
    {
        try
        {
            var state = (HostedSearch)GCHandle.FromIntPtr(lParam).Target!;
            var pid = Win32.GetWindowProcessId(hwnd);
            if (pid != 0 && pid != state.FramePid)
            {
                state.Found = pid;
                return 0; // stop
            }
            return 1;
        }
        catch
        {
            return 0; // never let an exception cross the native boundary
        }
    }

    private sealed class HostedSearch(uint framePid)
    {
        public uint FramePid { get; } = framePid;
        public uint Found { get; set; }
    }
}
