using System.Runtime.InteropServices;

namespace NotchHub.Platform.Media.Audio.Interop;

/// <summary>
/// Minimal PROPVARIANT (24 bytes on x64) able to read the value types used by audio endpoint property stores.
/// Always pass a value obtained from COM to <see cref="Clear"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    private const ushort VT_EMPTY = 0;
    private const ushort VT_I4 = 3;
    private const ushort VT_BOOL = 11;
    private const ushort VT_UI4 = 19;
    private const ushort VT_INT = 22;
    private const ushort VT_UINT = 23;
    private const ushort VT_LPWSTR = 31;
    private const ushort VT_CLSID = 72;

    [FieldOffset(0)] private ushort _vt;
    [FieldOffset(8)] private nint _pointer;
    [FieldOffset(8)] private int _int32;
    [FieldOffset(8)] private short _int16;
    // Keeps the struct as large as the native union (BLOB = { ULONG cbSize; BYTE* pBlobData; }).
    [FieldOffset(16)] private nint _pointer2;

    public readonly bool IsEmpty => _vt == VT_EMPTY;

    public readonly string? AsString() => _vt == VT_LPWSTR && _pointer != 0 ? Marshal.PtrToStringUni(_pointer) : null;

    public readonly uint? AsUInt32() => _vt switch
    {
        VT_UI4 or VT_UINT => unchecked((uint)_int32),
        VT_I4 or VT_INT => _int32 >= 0 ? (uint)_int32 : null,
        _ => null,
    };

    public readonly bool? AsBoolean() => _vt == VT_BOOL ? _int16 != 0 : null;

    public readonly Guid? AsGuid() => _vt == VT_CLSID && _pointer != 0 ? Marshal.PtrToStructure<Guid>(_pointer) : null;

    /// <summary>Readable rendering for diagnostics (harness property dumps).</summary>
    public readonly string Describe() => _vt switch
    {
        VT_EMPTY => "<empty>",
        VT_LPWSTR => AsString() ?? "<null>",
        VT_UI4 or VT_UINT or VT_I4 or VT_INT => _int32.ToString(System.Globalization.CultureInfo.InvariantCulture),
        VT_BOOL => (_int16 != 0).ToString(),
        VT_CLSID => AsGuid()?.ToString("B") ?? "<null>",
        _ => $"<vt {_vt}>",
    };

    /// <summary>Frees memory owned by the variant (strings, blobs) and resets it to VT_EMPTY.</summary>
    public void Clear()
    {
        if (_vt != VT_EMPTY) _ = PropVariantNative.PropVariantClear(ref this);
        _pointer2 = 0;
    }
}

internal static partial class PropVariantNative
{
    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant variant);
}
