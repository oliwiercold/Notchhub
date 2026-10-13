using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace NotchHub.Platform.Media.Common;

/// <summary>Minimal PNG encoder for small 32-bit images (icons): RGBA, 8 bits per channel, no filtering.</summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes top-down BGRA pixels with straight (non-premultiplied) alpha.</summary>
    public static byte[] EncodeBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer does not match the image size.", nameof(bgra));

        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var dst = y * (stride + 1);
            raw[dst++] = 0; // filter type: None
            var row = bgra.Slice(y * stride, stride);
            for (var x = 0; x < stride; x += 4)
            {
                raw[dst++] = row[x + 2];
                raw[dst++] = row[x + 1];
                raw[dst++] = row[x];
                raw[dst++] = row[x + 3];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // color type: truecolor + alpha
        header[10] = 0; // compression
        header[11] = 0; // filter method
        header[12] = 0; // interlace

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(png, "IEND", ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = UpdateCrc(0xFFFFFFFFu, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

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
}
