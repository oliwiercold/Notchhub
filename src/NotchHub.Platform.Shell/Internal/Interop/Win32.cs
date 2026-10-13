using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotchHub.Platform.Shell.Internal.Interop;

/// <summary>Win32 entry points used by the shell services (user32, kernel32, shell32, dwmapi).</summary>
internal static unsafe partial class Win32
{
    // ---- messages --------------------------------------------------------------------------
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_SYSCOLORCHANGE = 0x0015;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    // ---- window styles / misc ----------------------------------------------------------------
    public const int GWL_STYLE = -16;
    public const uint WS_POPUP = 0x8000_0000;
    public const uint WS_CAPTION = 0x00C0_0000;
    public const int WS_EX_TOOLWINDOW = 0x0000_0080;
    public const int WS_EX_NOACTIVATE = 0x0800_0000;
    public static readonly nint HWND_MESSAGE = -3;
    public const int SW_RESTORE = 9;
    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const int DWMWA_CLOAKED = 14;
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // ---- WinEvents -----------------------------------------------------------------------------
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    // ---- hotkeys ----------------------------------------------------------------------------
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    // ---- input ------------------------------------------------------------------------------
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint MAPVK_VK_TO_VSC = 0;

    // ---- system parameters ----------------------------------------------------------------------
    public const uint SPI_GETDESKWALLPAPER = 0x0073;
    public const uint SPI_SETDESKWALLPAPER = 0x0014;

    // ---- process ----------------------------------------------------------------------------
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ---- user32: clipboard ------------------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AddClipboardFormatListener(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveClipboardFormatListener(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    public static partial nint GetClipboardOwner();

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterClipboardFormat(string format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsClipboardFormatAvailable(uint format);

    // ---- user32: windows --------------------------------------------------------------------

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attachFrom, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* className, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumChildWindows(nint parent, delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    public static partial nint SetThreadDpiAwarenessContext(nint context);

    // ---- user32: hotkeys / input --------------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    public static partial uint MapVirtualKey(uint code, uint mapType);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    public static partial uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [LibraryImport("user32.dll")]
    public static partial int GetKeyboardLayoutList(int count, nint* layouts);

    [LibraryImport("user32.dll")]
    public static partial int ToUnicodeEx(uint virtualKey, uint scanCode, byte* keyState, char* buffer, int bufferSize, uint flags, nint layout);

    // ---- user32: WinEvents ----------------------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module,
        delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback,
        uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    // ---- user32: system parameters -----------------------------------------------------------

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint param, void* pvParam, uint winIni);

    // ---- kernel32 -------------------------------------------------------------------------------

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* exeName, ref uint size);

    // ---- shell32 / dwmapi -------------------------------------------------------------------------

    /// <summary>QUERY_USER_NOTIFICATION_STATE values we react to.</summary>
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, void* value, int size);

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Window class name, or an empty string.</summary>
    public static string GetClassName(nint hwnd)
    {
        if (hwnd == 0) return string.Empty;
        char* buffer = stackalloc char[256];
        var length = GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>True when DWM hides the window (suspended UWP apps, other virtual desktops).</summary>
    public static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    public static uint GetWindowProcessId(nint hwnd)
    {
        if (hwnd == 0) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    /// <summary>Full image path of a process (works for most elevated processes too), or null.</summary>
    public static string? GetProcessImagePath(uint processId)
    {
        if (processId == 0) return null;
        using var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle.IsInvalid) return null;
        char* buffer = stackalloc char[1024];
        uint size = 1024;
        return QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0 ? new string(buffer, 0, (int)size) : null;
    }

    /// <summary>Wallpaper path reported by SystemParametersInfo (empty for a solid color).</summary>
    public static string? GetDeskWallpaperPath()
    {
        const int capacity = 2048;
        char* buffer = stackalloc char[capacity];
        buffer[0] = '\0';
        if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, capacity, buffer, 0)) return null;
        return new string(buffer);
    }

    public static bool TryGetMonitorRect(nint monitor, out RECT rect)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor != 0 && GetMonitorInfo(monitor, ref info))
        {
            rect = info.rcMonitor;
            return true;
        }
        rect = default;
        return false;
    }

    /// <summary>Runs the scope with per-monitor-v2 DPI awareness so window and monitor rectangles are in physical pixels.</summary>
    public readonly struct PerMonitorDpiScope : IDisposable
    {
        private readonly nint _previous;

        private PerMonitorDpiScope(nint previous) => _previous = previous;

        public static PerMonitorDpiScope Enter() => new(SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));

        public void Dispose()
        {
            if (_previous != 0) SetThreadDpiAwarenessContext(_previous);
        }
    }

    /// <summary>Last Win32 error of the previous P/Invoke (for log messages).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LastError() => Marshal.GetLastPInvokeError();
}
