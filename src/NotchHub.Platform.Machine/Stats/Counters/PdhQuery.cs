using NotchHub.Core;

namespace NotchHub.Platform.Machine.Stats.Counters;

/// <summary>
/// Thin owner of a PDH query handle. Counters are added by English path (works on localized Windows).
/// Not thread-safe: the stats sampler uses each query from one sampling pass at a time.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private nint _handle;

    private PdhQuery(nint handle) => _handle = handle;

    public static PdhQuery? TryOpen()
    {
        try
        {
            var status = Interop.Pdh.PdhOpenQuery(null, 0, out var handle);
            if (status == 0) return new PdhQuery(handle);
            Log.Warn($"Stats: PdhOpenQuery failed (0x{status:X8})");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("Stats: PDH is not available", ex);
        }
        return null;
    }

    /// <summary>Adds a counter; returns null (and logs) when the counter does not exist on this machine.</summary>
    public PdhCounter? TryAddCounter(string englishPath)
    {
        if (_handle == 0) return null;
        var status = Interop.Pdh.PdhAddEnglishCounter(_handle, englishPath, 0, out var counter);
        if (status == 0) return new PdhCounter(counter);
        Log.Warn($"Stats: PDH counter '{englishPath}' unavailable (0x{status:X8})");
        return null;
    }

    /// <summary>Collects a new raw sample for every counter of the query.</summary>
    public bool Collect() => _handle != 0 && Interop.Pdh.PdhCollectQueryData(_handle) == 0;

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0) Interop.Pdh.PdhCloseQuery(handle);
    }
}

/// <summary>Visitor for wildcard counter instances; the name span is only valid during the call.</summary>
internal delegate void PdhInstanceVisitor(ReadOnlySpan<char> instance, double value);

/// <summary>A counter owned by a <see cref="PdhQuery"/> (closed together with the query).</summary>
internal sealed unsafe class PdhCounter
{
    private readonly nint _handle;
    private byte[] _arrayBuffer = [];

    public PdhCounter(nint handle) => _handle = handle;

    /// <summary>Formatted value of a single-instance counter, or null when no valid data is available yet.</summary>
    public double? GetDouble()
    {
        var status = Interop.Pdh.PdhGetFormattedCounterValue(_handle, Interop.Pdh.PDH_FMT_DOUBLE | Interop.Pdh.PDH_FMT_NOCAP100, out _, out var value);
        if (status != 0 || !IsValid(value.CStatus)) return null;
        var d = value.doubleValue;
        return double.IsFinite(d) ? d : null;
    }

    /// <summary>Enumerates every instance of a wildcard counter. Returns the number of valid instances visited.</summary>
    public int ForEachInstance(PdhInstanceVisitor visitor)
    {
        const int maxAttempts = 4;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var size = (uint)_arrayBuffer.Length;
            int status;
            uint count;
            fixed (byte* buffer = _arrayBuffer)
            {
                status = Interop.Pdh.PdhGetFormattedCounterArray(_handle, Interop.Pdh.PDH_FMT_DOUBLE | Interop.Pdh.PDH_FMT_NOCAP100,
                    ref size, out count, size == 0 ? null : buffer);
                if (status == 0)
                {
                    var items = (Interop.Pdh.PDH_FMT_COUNTERVALUE_ITEM_W*)buffer;
                    var visited = 0;
                    for (var i = 0; i < count; i++)
                    {
                        if (!IsValid(items[i].FmtValue.CStatus) || items[i].szName == null) continue;
                        var value = items[i].FmtValue.doubleValue;
                        if (!double.IsFinite(value)) continue;
                        visitor(new ReadOnlySpan<char>(items[i].szName, StrLen(items[i].szName)), value);
                        visited++;
                    }
                    return visited;
                }
            }
            if (status != Interop.Pdh.PDH_MORE_DATA) return 0;
            // Instances can appear between the size query and the read: leave some headroom.
            _arrayBuffer = new byte[size + 8192];
        }
        return 0;
    }

    private static bool IsValid(uint cstatus) => cstatus is Interop.Pdh.PDH_CSTATUS_VALID_DATA or Interop.Pdh.PDH_CSTATUS_NEW_DATA;

    private static int StrLen(char* s)
    {
        var n = 0;
        while (s[n] != '\0') n++;
        return n;
    }
}
