using NotchHub.Core;

namespace NotchHub.Platform.Media.Common;

/// <summary>Extracts icons from executables / resource DLLs and converts them to PNG using plain GDI.</summary>
internal static unsafe class IconPng
{
    private const int MaxIconSize = 256;

    /// <summary>
    /// Icon <paramref name="index"/> of <paramref name="file"/> (negative = resource id) scaled to
    /// <paramref name="size"/> pixels, as PNG; null when the file has no such icon.
    /// </summary>
    public static byte[]? FromFile(string file, int index, int size)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        nint icon = 0;
        uint iconId = 0;
        uint extracted;
        try
        {
            extracted = Win32.PrivateExtractIcons(file, index, size, size, &icon, &iconId, 1, 0);
        }
        catch (Exception ex) when (ex is ArgumentException or EntryPointNotFoundException)
        {
            return null;
        }
        if (extracted == 0 || extracted == uint.MaxValue || icon == 0) return null;
        try
        {
            return FromHIcon(icon);
        }
        finally
        {
            Win32.DestroyIcon(icon);
        }
    }

    /// <summary>
    /// Parses an icon location such as "@%SystemRoot%\System32\AudioSrv.Dll,-203" or "C:\app.exe,0" and extracts it.
    /// </summary>
    public static byte[]? FromIconLocation(string? location, int size)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var text = location.Trim().TrimStart('@').Trim('"');
        var index = 0;
        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text.AsSpan(comma + 1), out var parsed))
        {
            index = parsed;
            text = text[..comma];
        }
        var path = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
        return File.Exists(path) ? FromFile(path, index, size) : null;
    }

    /// <summary>Converts an HICON (not destroyed by this method) to PNG; null for monochrome / invalid icons.</summary>
    public static byte[]? FromHIcon(nint icon)
    {
        if (!Win32.GetIconInfo(icon, out var info)) return null;
        try
        {
            if (info.hbmColor == 0) return null; // monochrome icon: AND/XOR masks only

            Win32.BITMAP bitmap;
            if (Win32.GetObject(info.hbmColor, sizeof(Win32.BITMAP), &bitmap) == 0) return null;
            int width = bitmap.bmWidth, height = Math.Abs(bitmap.bmHeight);
            if (width is <= 0 or > MaxIconSize || height is <= 0 or > MaxIconSize) return null;

            var pixels = new byte[width * height * 4];
            if (!ReadBitmap(info.hbmColor, width, height, pixels)) return null;

            if (!HasAlpha(pixels) && !ApplyMask(info.hbmMask, width, height, pixels)) return null;
            return PngWriter.EncodeBgra(pixels, width, height);
        }
        catch (Exception ex)
        {
            Log.Debug($"IconPng: conversion failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (info.hbmColor != 0) Win32.DeleteObject(info.hbmColor);
            if (info.hbmMask != 0) Win32.DeleteObject(info.hbmMask);
        }
    }

    private static bool HasAlpha(ReadOnlySpan<byte> bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
            if (bgra[i] != 0) return true;
        return false;
    }

    /// <summary>Legacy icons without an alpha channel: AND mask pixel set (white) = transparent.</summary>
    private static bool ApplyMask(nint maskBitmap, int width, int height, Span<byte> bgra)
    {
        if (maskBitmap == 0) return false;
        var mask = new byte[width * height * 4];
        if (!ReadBitmap(maskBitmap, width, height, mask)) return false;
        for (var i = 0; i < bgra.Length; i += 4)
            bgra[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
        return true;
    }

    /// <summary>Reads any GDI bitmap as top-down 32-bit BGRA.</summary>
    private static bool ReadBitmap(nint bitmap, int width, int height, Span<byte> destination)
    {
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = 40, // the header proper; the extra fields are slack for GetDIBits
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };
        var dc = Win32.GetDC(0);
        if (dc == 0) return false;
        try
        {
            fixed (byte* bits = destination)
                return Win32.GetDIBits(dc, bitmap, 0, (uint)height, bits, &header, 0 /* DIB_RGB_COLORS */) == height;
        }
        finally
        {
            Win32.ReleaseDC(0, dc);
        }
    }
}
