using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace NotchHub.Platform.Media.Common;

/// <summary>
/// Friendly application names from executables, AppUserModelIDs, indirect resource strings and the shell's
/// AppsFolder. All lookups are cached and never throw.
/// </summary>
internal static unsafe class AppNames
{
    private static readonly ConcurrentDictionary<string, string?> DescriptionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string?> PackagedNameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>FileDescription (falls back to ProductName) of an executable.</summary>
    public static string? FileDescription(string exePath) =>
        DescriptionCache.GetOrAdd(exePath, static path =>
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return NullIfBlank(info.FileDescription) ?? NullIfBlank(info.ProductName);
            }
            catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        });

    /// <summary>Display name of a packaged app (MSIX/UWP) from its AppUserModelID, e.g. "Media Player".</summary>
    public static string? PackagedDisplayName(string appUserModelId) =>
        PackagedNameCache.GetOrAdd(appUserModelId, static aumid =>
        {
            try
            {
                return NullIfBlank(AppInfo.GetFromAppUserModelId(aumid)?.DisplayInfo?.DisplayName);
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
            {
                return null;
            }
        });

    /// <summary>Resolves "@dll,-id" / "@{package?ms-resource://…}" strings; other values are returned unchanged.</summary>
    public static string? ResolveIndirect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value[0] != '@') return value.Trim();

        const int capacity = 512;
        var buffer = stackalloc char[capacity];
        var source = Environment.ExpandEnvironmentVariables(value);
        var hr = Win32.SHLoadIndirectString(source, buffer, capacity, 0);
        return hr == 0 ? NullIfBlank(new string(buffer)) : null;
    }

    /// <summary>
    /// Name the shell shows for an app in Start (shell:AppsFolder\{aumid}); works for packaged apps and for
    /// desktop apps whose Start menu shortcut carries an AppUserModelID (Chrome, Firefox...).
    /// </summary>
    public static string? ShellAppsFolderName(string appUserModelId)
    {
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(@"shell:AppsFolder\" + appUserModelId, 0, ref iid, out var item);
            try
            {
                item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var namePtr);
                try { return NullIfBlank(Marshal.PtrToStringUni(namePtr)); }
                finally { Marshal.FreeCoTaskMem(namePtr); }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or ArgumentException or UnauthorizedAccessException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>Expands an AUMID-style path whose first segment is a known folder GUID ("{6D809377-…}\VLC\vlc.exe").</summary>
    public static string? ExpandKnownFolderPath(string value)
    {
        if (value.Length < 39 || value[0] != '{') return value;
        var close = value.IndexOf('}');
        if (close != 37 || !Guid.TryParse(value.AsSpan(0, close + 1), out var folderId)) return value;

        if (Win32.SHGetKnownFolderPath(folderId, 0, 0, out var pathPtr) != 0) return null;
        try
        {
            var root = Marshal.PtrToStringUni(pathPtr);
            return root is null ? null : Path.Combine(root, value[(close + 1)..].TrimStart('\\', '/'));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    /// <summary>Last-resort readable name from an identifier ("SpotifyAB.SpotifyMusic_x!Spotify" → "Spotify").</summary>
    public static string Prettify(string id)
    {
        var name = id;
        var bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1) name = name[(bang + 1)..];
        var slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0 && slash < name.Length - 1) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1 && bang >= 0) name = name[(dot + 1)..];
        name = name.Trim();
        if (name.Length == 0) return id;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint bindContext, ref Guid handlerId, ref Guid riid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(int sigdnName, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
