using System.Buffers.Binary;
using System.IO.Compression;
using NotchHub.Core;
using NotchHub.Platform.Machine.Interop;

namespace NotchHub.Platform.Machine.Stats.Processes;

/// <summary>32×32 PNG icons of executables, extracted with SHDefExtractIcon and rendered through a DIB. Cached per path.</summary>
internal sealed unsafe class IconCache
{
    private const int Size = 32;
    private readonly Dictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? Get(string exePath)
    {
        if (_cache.TryGetValue(exePath, out var png)) return png;
        try { png = Extract(exePath); }
        catch (Exception ex)
        {
            Log.Warn("Stats: icon extraction failed", ex);
            png = null;
        }
        if (_cache.Count > 512) _cache.Clear();
        _cache[exePath] = png;
        return png;
    }

    private static byte[]? Extract(string path)
    {
        nint icon = 0;
        if (Shell32.SHDefExtractIcon(path, 0, 0, &icon, null, Size) != 0 || icon == 0) return null;
        var dc = Gdi32.CreateCompatibleDC(0);
        nint bitmap = 0;
        try
        {
            var info = new Gdi32.BITMAPINFO
            {
                bmiHeader = new Gdi32.BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(Gdi32.BITMAPINFOHEADER),
                    biWidth = Size,
                    biHeight = -Size, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Gdi32.BI_RGB,
                },
            };
            void* bits;
            bitmap = Gdi32.CreateDIBSection(dc, &info, Gdi32.DIB_RGB_COLORS, &bits, 0, 0);
            if (bitmap == 0 || bits is null) return null;
            var old = Gdi32.SelectObject(dc, bitmap);
            User32.DrawIconEx(dc, 0, 0, icon, Size, Size, 0, 0, User32.DI_NORMAL);
            Gdi32.GdiFlush();
            Gdi32.SelectObject(dc, old);

            var pixels = new ReadOnlySpan<byte>(bits, Size * Size * 4).ToArray();
            var hasAlpha = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (!hasAlpha)
                {
                    // Legacy icons without alpha: treat drawn (non-black) pixels as opaque.
                    pixels[i + 3] = (byte)(pixels[i] | pixels[i + 1] | pixels[i + 2]) == 0 ? (byte)0 : (byte)255;
                    continue;
                }
                var a = pixels[i + 3];
                if (a is 0 or 255) continue;
                // DIB output is premultiplied; PNG wants straight alpha.
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
            return EncodePng(pixels, Size, Size);
        }
        finally
        {
            if (bitmap != 0) Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(dc);
            User32.DestroyIcon(icon);
        }
    }

    /// <summary>Minimal RGBA8 PNG encoder (filter 0, zlib).</summary>
    internal static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[width * 4 + 1];
            for (var y = 0; y < height; y++)
            {
                row[0] = 0;
                for (var x = 0; x < width; x++)
                {
                    var s = (y * width + x) * 4;
                    var d = 1 + x * 4;
                    row[d] = bgra[s + 2];
                    row[d + 1] = bgra[s + 1];
                    row[d + 2] = bgra[s];
                    row[d + 3] = bgra[s + 3];
                }
                z.Write(row);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        WriteChunk(png, "IHDR"u8, ihdr);
        WriteChunk(png, "IDAT"u8, raw.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static void WriteChunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        s.Write(type);
        s.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(len, crc ^ 0xFFFFFFFFu);
        s.Write(len);
    }
}
