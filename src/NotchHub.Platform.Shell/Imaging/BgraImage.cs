namespace NotchHub.Platform.Shell.Imaging;

/// <summary>Uncompressed 32-bit image, straight (non-premultiplied) alpha, B,G,R,A byte order, stride = Width * 4.</summary>
internal sealed class BgraImage
{
    public BgraImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length < (long)width * height * 4) throw new ArgumentException("Pixel buffer too small.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public int Stride => Width * 4;

    /// <summary>Some sources (32-bit DIBs) leave alpha at 0 everywhere; such images are meant to be opaque.</summary>
    public void FixAllTransparent()
    {
        var px = Pixels;
        for (var i = 3; i < px.Length; i += 4)
        {
            if (px[i] != 0) return;
        }
        for (var i = 3; i < px.Length; i += 4) px[i] = 255;
    }

    /// <summary>Converts to row-major 0xAARRGGBB values.</summary>
    public uint[] ToArgb()
    {
        var px = Pixels;
        var result = new uint[Width * Height];
        for (int i = 0, p = 0; i < result.Length; i++, p += 4)
        {
            result[i] = (uint)px[p + 3] << 24 | (uint)px[p + 2] << 16 | (uint)px[p + 1] << 8 | px[p];
        }
        return result;
    }
}
