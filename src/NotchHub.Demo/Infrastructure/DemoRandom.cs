namespace NotchHub.Demo.Infrastructure;

/// <summary>
/// Deterministic random sources. Every stream (e.g. "stats", "phone") gets its own <see cref="Random"/> seeded from a
/// fixed base seed and a stable hash of the stream name, so demo data and screenshots are identical on every run and
/// one service consuming more numbers never shifts the values of another.
/// </summary>
internal static class DemoRandom
{
    private const int BaseSeed = 0x5EED_2026;

    /// <summary>New generator for <paramref name="stream"/>. <see cref="Random"/> is not thread-safe: callers lock.</summary>
    public static Random For(string stream) => new(BaseSeed ^ StableHash(stream));

    // string.GetHashCode is randomized per process, so use FNV-1a instead.
    private static int StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return (int)hash;
    }
}
