using System.Runtime.InteropServices;

namespace NotchHub.Platform.Machine.Interop;

internal static unsafe partial class Shell32
{
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    /// <summary>Extracts an icon from a file at an exact pixel size (LOWORD = large size, HIWORD = small size).</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHDefExtractIcon(string iconFile, int index, uint flags, nint* large, nint* small, uint iconSize);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SHGetFileInfo(string path, uint fileAttributes, SHFILEINFOW* info, uint fileInfoSize, uint flags);
}
