namespace NotchHub.Platform.Connectivity.Display.NightLight;

/// <summary>
/// Decoded night light state CloudStore value (<c>...bluelightreductionstate</c> → <c>Data</c>). The value is a Bond
/// Compact Binary v1 struct (see <see cref="BondCompactBinary"/>):
/// <code>
/// outer { 0: struct { 0: bool }
///         1: struct { 0: uint64  Unix seconds of the last write (a write with a value not greater than the stored
///                                one is reverted by Windows, which itself uses max(now, previous + 2))
///                     1: struct { 1: blob = inner payload } } }
/// inner { 0:  int32 0 — present ⇔ night light is active
///         10: int32 1 — present ⇔ the last transition was manual (absent: caused by the schedule)
///         20: uint64  FILETIME (UTC) of the last transition }
/// </code>
/// The per-device variant (Windows 11 24H2+) wraps the inner fields in a base-struct section; both are handled.
/// Unknown fields are preserved when the value is rebuilt. Instances are immutable.
/// </summary>
internal sealed class NightLightBlob
{
    private const ushort ActiveFieldId = 0;
    private const ushort ManualFieldId = 10;
    private const ushort FileTimeFieldId = 20;

    private readonly byte[] _data;

    private NightLightBlob(byte[] data, bool isActive, bool isManual, ulong unixSeconds, ulong fileTime)
    {
        _data = data;
        IsActive = isActive;
        IsManual = isManual;
        UnixSeconds = unixSeconds;
        FileTime = fileTime;
    }

    /// <summary>Night light is currently on.</summary>
    public bool IsActive { get; }

    /// <summary>The last transition was made by the user (not by the schedule).</summary>
    public bool IsManual { get; }

    /// <summary>Write timestamp of the CloudStore value (Unix seconds).</summary>
    public ulong UnixSeconds { get; }

    /// <summary>Precise time of the last transition (FILETIME, UTC).</summary>
    public ulong FileTime { get; }

    /// <summary>Parses <paramref name="data"/>; null when it does not have the expected structure.</summary>
    public static NightLightBlob? TryParse(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        try
        {
            var layout = Layout.Decode(data);
            if (layout is null) return null;
            var active = layout.Section.Find(f => f.Id == ActiveFieldId) is not null;
            var manual = layout.Section.Find(f => f.Id == ManualFieldId) is not null;
            return new NightLightBlob((byte[])data.Clone(), active, manual, (ulong)layout.Timestamp.Value, (ulong)layout.FileTime.Value);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the value Windows' own toggle would write: active flag set/cleared, transition marked manual, transition
    /// time = <paramref name="now"/> and a write timestamp strictly newer than the current one.
    /// </summary>
    public byte[] BuildToggled(bool active, DateTimeOffset now)
    {
        var layout = Layout.Decode(_data) ?? throw new InvalidOperationException("Night light value changed shape.");
        var section = layout.Section;

        section.RemoveAll(f => f.Id == ActiveFieldId);
        if (active) InsertSorted(section, new BondField(ActiveFieldId, BondType.Int32, 0L));

        var manual = section.Find(f => f.Id == ManualFieldId);
        if (manual is null) InsertSorted(section, new BondField(ManualFieldId, BondType.Int32, 1L));
        else manual.Value = 1L;

        layout.FileTime.Value = (ulong)now.UtcDateTime.ToFileTimeUtc();
        layout.Payload.Value = BondCompactBinary.EncodeMarshaled(layout.Inner);

        var nowSeconds = (ulong)Math.Max(0, now.ToUnixTimeSeconds());
        layout.Timestamp.Value = Math.Max(nowSeconds, (ulong)layout.Timestamp.Value + 2);
        return BondCompactBinary.EncodeMarshaled(layout.Outer);
    }

    /// <summary>The raw bytes this instance was parsed from.</summary>
    public byte[] ToArray() => (byte[])_data.Clone();

    private static void InsertSorted(List<BondField> section, BondField field)
    {
        var index = section.FindIndex(f => f.Id > field.Id);
        section.Insert(index < 0 ? section.Count : index, field);
    }

    /// <summary>The fields of interest inside a freshly decoded (mutable) tree.</summary>
    private sealed record Layout(
        BondStruct Outer, BondField Timestamp, BondField Payload, BondStruct Inner, List<BondField> Section, BondField FileTime)
    {
        public static Layout? Decode(byte[] data)
        {
            var outer = BondCompactBinary.DecodeMarshaled(data);
            if (outer.Find(1) is not { Type: BondType.Struct, Value: BondStruct header }) return null;
            if (header.Find(0) is not { Type: BondType.UInt64 } timestamp) return null;
            if (header.Find(1) is not { Type: BondType.Struct, Value: BondStruct body }) return null;
            if (body.Find(1) is not { Type: BondType.List, Value: byte[] payloadBytes } payload) return null;

            var inner = BondCompactBinary.DecodeMarshaled(payloadBytes);
            var section = inner.Sections.FirstOrDefault(s => s.Exists(f => f.Id == FileTimeFieldId));
            if (section is null) return null;
            var fileTime = section.Find(f => f.Id == FileTimeFieldId)!;
            if (fileTime.Type != BondType.UInt64) return null;
            if (section.Find(f => f.Id == ActiveFieldId) is { Type: not BondType.Int32 }) return null;
            if (section.Find(f => f.Id == ManualFieldId) is { Type: not BondType.Int32 }) return null;

            return new Layout(outer, timestamp, payload, inner, section, fileTime);
        }
    }
}
