using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NotchHub.Installer.Engine;

/// <summary>Finds and closes running NotchHub instances (graceful WM_CLOSE first, kill after a timeout).</summary>
public static class AppProcesses
{
    private const uint WM_CLOSE = 0x0010;

    /// <summary>
    /// Running NotchHub.exe processes. With <paramref name="onlyUnder"/> set (dry-run) only instances started from that folder
    /// are returned — a test run never touches the user's real instance.
    /// </summary>
    public static List<Process> Find(string? onlyUnder)
    {
        var result = new List<Process>();
        foreach (var p in Process.GetProcessesByName("NotchHub"))
        {
            if (p.Id == Environment.ProcessId) { p.Dispose(); continue; }
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* other session / access denied */ }
            if (onlyUnder is not null && !FileOps.IsUnder(path, onlyUnder))
            {
                p.Dispose();
                continue;
            }
            result.Add(p);
        }
        return result;
    }

    public static bool AnyRunning(string? onlyUnder)
    {
        var list = Find(onlyUnder);
        foreach (var p in list) p.Dispose();
        return list.Count > 0;
    }

    /// <summary>Asks every instance to close (WM_CLOSE to its top-level windows), kills what is left after the timeout.</summary>
    public static void CloseAll(string? onlyUnder, TimeSpan timeout)
    {
        var list = Find(onlyUnder);
        if (list.Count == 0) return;
        foreach (var p in list)
        {
            var pid = (uint)p.Id;
            var count = 0;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var owner);
                if (owner == pid)
                {
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    count++;
                }
                return true;
            }, IntPtr.Zero);
            SetupLog.Info($"Close NotchHub pid {pid}: WM_CLOSE sent to {count} window(s)");
        }
        var deadline = DateTime.UtcNow + timeout;
        foreach (var p in list)
        {
            try
            {
                var left = deadline - DateTime.UtcNow;
                if (!p.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero))
                {
                    SetupLog.Warn($"NotchHub pid {p.Id} did not exit in time — killing it");
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                SetupLog.Warn($"Closing pid {SafeId(p)} failed", ex);
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private static int SafeId(Process p)
    {
        try { return p.Id; } catch { return -1; }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
