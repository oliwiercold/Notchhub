using System.Runtime.InteropServices;

namespace NotchHub.Platform.Machine.Interop;

internal static partial class User32
{
    public const uint EWX_LOGOFF = 0x00000000;
    public const uint EWX_SHUTDOWN = 0x00000001;
    public const uint EWX_REBOOT = 0x00000002;
    public const uint EWX_POWEROFF = 0x00000008;
    public const uint EWX_HYBRID_SHUTDOWN = 0x00400000;

    public const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;
    public const uint SHTDN_REASON_MINOR_OTHER = 0x00000000;
    public const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    public const uint DI_MASK = 0x0001;
    public const uint DI_NORMAL = 0x0003;

    public const uint WM_SYSCOMMAND = 0x0112;
    public const nint SC_MONITORPOWER = 0xF170;
    public const nint HWND_BROADCAST = 0xFFFF;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ExitWindowsEx(uint flags, uint reason);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(nint hdc, int left, int top, nint icon, int width, int height, uint stepIfAniCur, nint flickerFreeBrush, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}
