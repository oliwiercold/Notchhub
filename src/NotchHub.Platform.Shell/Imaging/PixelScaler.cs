namespace NotchHub.Platform.Shell.Imaging;

/// <summary>High-quality downscaling by exact area averaging (box filter with fractional coverage), alpha-weighted.</summary>
internal static class PixelScaler
{
    /// <summary>Target size that fits <paramref name="maxSide"/> while keeping the aspect ratio. Never upscales.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxSide)
    {
        maxSide = Math.Max(1, maxSide);
        var longest = Math.Max(width, height);
        if (longest <= maxSide) return (width, height);
        var scale = (double)maxSide / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Downscales <paramref name="source"/> to exactly <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static BgraImage Downscale(BgraImage source, int width, int height)
    {
        if (width >= source.Width && height >= source.Height) return source;
        width = Math.Clamp(width, 1, source.Width);
        height = Math.Clamp(height, 1, source.Height);

        var xSpans = BuildSpans(source.Width, width);
        var ySpans = BuildSpans(source.Height, height);

        // Pass 1: horizontal — accumulate alpha-weighted colors into one row of doubles per target column.
        var src = source.Pixels;
        var output = new byte[width * height * 4];
        var row = new double[width * 4]; // premultiplied B,G,R + A
        var acc = new double[width * 4];

        for (var ty = 0; ty < height; ty++)
        {
            Array.Clear(acc);
            var ySpan = ySpans[ty];
            for (var sy = ySpan.Start; sy < ySpan.End; sy++)
            {
                var wy = ySpan.Weight(sy);
                HorizontalPass(src, sy * source.Stride, xSpans, row);
                for (var i = 0; i < acc.Length; i++) acc[i] += row[i] * wy;
            }

            var area = ySpan.Total;
            var o = ty * width * 4;
            for (var tx = 0; tx < width; tx++)
            {
                var j = tx * 4;
                var a = acc[j + 3];
                var totalWeight = xSpans[tx].Total * area;
                if (a <= 1e-9)
                {
                    output[o + j] = output[o + j + 1] = output[o + j + 2] = output[o + j + 3] = 0;
                    continue;
                }
                output[o + j] = ToByte(acc[j] / a);
                output[o + j + 1] = ToByte(acc[j + 1] / a);
                output[o + j + 2] = ToByte(acc[j + 2] / a);
                output[o + j + 3] = ToByte(a / totalWeight);
            }
        }
        return new BgraImage(width, height, output);
    }

    private static void HorizontalPass(byte[] src, int rowOffset, Span[] xSpans, double[] row)
    {
        for (var tx = 0; tx < xSpans.Length; tx++)
        {
            var span = xSpans[tx];
            double b = 0, g = 0, r = 0, a = 0;
            for (var sx = span.Start; sx < span.End; sx++)
            {
                var w = span.Weight(sx);
                var p = rowOffset + sx * 4;
                var alpha = src[p + 3] * w;
                b += src[p] * alpha;
                g += src[p + 1] * alpha;
                r += src[p + 2] * alpha;
                a += alpha;
            }
            var j = tx * 4;
            row[j] = b;
            row[j + 1] = g;
            row[j + 2] = r;
            row[j + 3] = a;
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    /// <summary>For each target index: the source range [Start, End) it covers, with fractional edge weights.</summary>
    private static Span[] BuildSpans(int sourceLength, int targetLength)
    {
        var spans = new Span[targetLength];
        var scale = (double)sourceLength / targetLength;
        for (var t = 0; t < targetLength; t++)
        {
            var from = t * scale;
            var to = Math.Min(sourceLength, (t + 1) * scale);
            spans[t] = new Span(from, to);
        }
        return spans;
    }

    private readonly struct Span
    {
        private readonly double _from;
        private readonly double _to;

        public Span(double from, double to)
        {
            _from = from;
            _to = to;
            Start = (int)Math.Floor(from);
            End = Math.Max(Start + 1, (int)Math.Ceiling(to));
            Total = to - from;
        }

        public int Start { get; }
        public int End { get; }
        public double Total { get; }

        /// <summary>Coverage of source pixel <paramref name="index"/> by this span (0..1).</summary>
        public double Weight(int index) => Math.Max(0, Math.Min(index + 1, _to) - Math.Max(index, _from));
    }
}
