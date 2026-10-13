using System.Buffers.Binary;

namespace NotchHub.Platform.Connectivity.Display.NightLight;

/// <summary>Bond wire data types (<c>BondDataType</c>).</summary>
internal enum BondType : byte
{
    Stop = 0,
    StopBase = 1,
    Bool = 2,
    UInt8 = 3,
    UInt16 = 4,
    UInt32 = 5,
    UInt64 = 6,
    Float = 7,
    Double = 8,
    String = 9,
    Struct = 10,
    List = 11,
    Set = 12,
    Map = 13,
    Int8 = 14,
    Int16 = 15,
    Int32 = 16,
    Int64 = 17,
    WString = 18,
}

/// <summary>
/// One field of a decoded Bond struct. <see cref="Value"/> holds: <see cref="ulong"/> for Bool/UInt8/UInt16/32/64,
/// <see cref="long"/> for Int8/16/32/64, <see cref="byte"/>[] for Float/Double/String/WString (raw payload) and for
/// lists/sets of int8 (blobs), <see cref="BondStruct"/>, <see cref="BondContainer"/> or <see cref="BondMap"/>.
/// </summary>
internal sealed class BondField(ushort id, BondType type, object value)
{
    public ushort Id { get; } = id;
    public BondType Type { get; } = type;
    public object Value { get; set; } = value;
}

/// <summary>
/// A decoded struct: its inheritance sections in wire order (base-most first). Every section but the last one was
/// terminated by <see cref="BondType.StopBase"/> on the wire.
/// </summary>
internal sealed class BondStruct
{
    public List<List<BondField>> Sections { get; } = [[]];

    /// <summary>First field with <paramref name="id"/>, searching base sections first.</summary>
    public BondField? Find(ushort id)
    {
        foreach (var section in Sections)
        {
            foreach (var field in section)
            {
                if (field.Id == id) return field;
            }
        }
        return null;
    }
}

/// <summary>A decoded list or set whose elements are not int8.</summary>
internal sealed class BondContainer(BondType elementType, List<object> items)
{
    public BondType ElementType { get; } = elementType;
    public List<object> Items { get; } = items;
}

/// <summary>A decoded map.</summary>
internal sealed class BondMap(BondType keyType, BondType valueType, List<KeyValuePair<object, object>> entries)
{
    public BondType KeyType { get; } = keyType;
    public BondType ValueType { get; } = valueType;
    public List<KeyValuePair<object, object>> Entries { get; } = entries;
}

/// <summary>
/// Minimal, loss-less codec for Microsoft Bond <b>Compact Binary v1</b> payloads with the marshaling header
/// (<c>43 42 01 00</c> = protocol "CB", version 1). Windows uses it for the CloudStore registry values. Decoding then
/// re-encoding yields the original bytes for canonical (minimal varint) input, so unknown fields are preserved.
/// Malformed input raises <see cref="FormatException"/>.
/// </summary>
internal static class BondCompactBinary
{
    private const ushort CompactProtocolMagic = 0x4243;
    private const ushort Version1 = 1;
    private const int MaxDepth = 32;

    /// <summary>Decodes a marshaled Compact Binary v1 struct; the whole buffer must be consumed.</summary>
    public static BondStruct DecodeMarshaled(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4
            || BinaryPrimitives.ReadUInt16LittleEndian(data) != CompactProtocolMagic
            || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != Version1)
        {
            throw new FormatException("Not a Bond Compact Binary v1 payload.");
        }

        var reader = new Reader(data, 4);
        var result = reader.ReadStruct(0);
        if (reader.Position != data.Length) throw new FormatException("Trailing bytes after the Bond struct.");
        return result;
    }

    /// <summary>Encodes <paramref name="value"/> with the Compact Binary v1 marshaling header.</summary>
    public static byte[] EncodeMarshaled(BondStruct value)
    {
        var writer = new Writer();
        writer.WriteUInt16(CompactProtocolMagic);
        writer.WriteUInt16(Version1);
        writer.WriteStruct(value, 0);
        return writer.ToArray();
    }

    private ref struct Reader(ReadOnlySpan<byte> data, int position)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; } = position;

        public BondStruct ReadStruct(int depth)
        {
            if (depth > MaxDepth) throw new FormatException("Bond nesting too deep.");
            var result = new BondStruct();
            while (true)
            {
                var header = ReadByte();
                var type = (BondType)(header & 0x1F);
                var idBits = header >> 5;
                if (type == BondType.Stop) return result;
                if (type == BondType.StopBase)
                {
                    result.Sections.Add([]);
                    continue;
                }

                ushort id = idBits switch
                {
                    6 => ReadByte(),
                    7 => ReadUInt16(),
                    _ => (ushort)idBits,
                };
                result.Sections[^1].Add(new BondField(id, type, ReadValue(type, depth)));
            }
        }

        private object ReadValue(BondType type, int depth) => type switch
        {
            BondType.Bool or BondType.UInt8 => (ulong)ReadByte(),
            BondType.Int8 => (long)(sbyte)ReadByte(),
            BondType.UInt16 or BondType.UInt32 or BondType.UInt64 => ReadVarUInt64(),
            BondType.Int16 or BondType.Int32 or BondType.Int64 => ZigZagDecode(ReadVarUInt64()),
            BondType.Float => ReadBytes(4),
            BondType.Double => ReadBytes(8),
            BondType.String => ReadBytes(ReadLength(1)),
            BondType.WString => ReadBytes(checked(ReadLength(2) * 2)),
            BondType.Struct => ReadStruct(depth + 1),
            BondType.List or BondType.Set => ReadContainer(depth),
            BondType.Map => ReadMap(depth),
            _ => throw new FormatException($"Unsupported Bond type {(int)type}."),
        };

        private object ReadContainer(int depth)
        {
            var elementType = (BondType)(ReadByte() & 0x1F);
            // Bond's "blob" is a list<int8>; expose it as raw bytes.
            if (elementType == BondType.Int8) return ReadBytes(ReadLength(1));

            var count = ReadLength(1);
            var items = new List<object>(count);
            for (var i = 0; i < count; i++) items.Add(ReadValue(elementType, depth + 1));
            return new BondContainer(elementType, items);
        }

        private BondMap ReadMap(int depth)
        {
            var keyType = (BondType)(ReadByte() & 0x1F);
            var valueType = (BondType)(ReadByte() & 0x1F);
            var count = ReadLength(2);
            var entries = new List<KeyValuePair<object, object>>(count);
            for (var i = 0; i < count; i++)
            {
                var key = ReadValue(keyType, depth + 1);
                entries.Add(new(key, ReadValue(valueType, depth + 1)));
            }
            return new BondMap(keyType, valueType, entries);
        }

        /// <summary>Reads a count and rejects values that cannot fit in the remaining input (each item ≥ <paramref name="minItemSize"/> bytes).</summary>
        private int ReadLength(int minItemSize)
        {
            var value = ReadVarUInt64();
            var remaining = (ulong)(_data.Length - Position);
            if (value > remaining / (ulong)minItemSize) throw new FormatException("Bond length exceeds the payload.");
            return (int)value;
        }

        private byte ReadByte()
        {
            if (Position >= _data.Length) throw new FormatException("Unexpected end of Bond payload.");
            return _data[Position++];
        }

        private ushort ReadUInt16()
        {
            if (Position + 2 > _data.Length) throw new FormatException("Unexpected end of Bond payload.");
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Position..]);
            Position += 2;
            return value;
        }

        private byte[] ReadBytes(int count)
        {
            if (count < 0 || Position + count > _data.Length) throw new FormatException("Unexpected end of Bond payload.");
            var bytes = _data.Slice(Position, count).ToArray();
            Position += count;
            return bytes;
        }

        private ulong ReadVarUInt64()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var b = ReadByte();
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
            }
            throw new FormatException("Bond varint too long.");
        }

        private static long ZigZagDecode(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
    }

    private sealed class Writer
    {
        private readonly List<byte> _buffer = new(64);

        public byte[] ToArray() => [.. _buffer];

        public void WriteUInt16(ushort value)
        {
            _buffer.Add((byte)value);
            _buffer.Add((byte)(value >> 8));
        }

        public void WriteStruct(BondStruct value, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("Bond nesting too deep.");
            for (var s = 0; s < value.Sections.Count; s++)
            {
                foreach (var field in value.Sections[s])
                {
                    WriteFieldHeader(field.Id, field.Type);
                    WriteValue(field.Type, field.Value, depth);
                }
                _buffer.Add((byte)(s == value.Sections.Count - 1 ? BondType.Stop : BondType.StopBase));
            }
        }

        private void WriteFieldHeader(ushort id, BondType type)
        {
            var t = (byte)type;
            if (id <= 5)
            {
                _buffer.Add((byte)((id << 5) | t));
            }
            else if (id <= 0xFF)
            {
                _buffer.Add((byte)(0xC0 | t));
                _buffer.Add((byte)id);
            }
            else
            {
                _buffer.Add((byte)(0xE0 | t));
                WriteUInt16(id);
            }
        }

        private void WriteValue(BondType type, object value, int depth)
        {
            switch (type)
            {
                case BondType.Bool or BondType.UInt8:
                    _buffer.Add((byte)(ulong)value);
                    break;
                case BondType.Int8:
                    _buffer.Add((byte)(sbyte)(long)value);
                    break;
                case BondType.UInt16 or BondType.UInt32 or BondType.UInt64:
                    WriteVarUInt64((ulong)value);
                    break;
                case BondType.Int16 or BondType.Int32 or BondType.Int64:
                    var signed = (long)value;
                    WriteVarUInt64((ulong)((signed << 1) ^ (signed >> 63)));
                    break;
                case BondType.Float or BondType.Double:
                    _buffer.AddRange((byte[])value);
                    break;
                case BondType.String:
                    var utf8 = (byte[])value;
                    WriteVarUInt64((ulong)utf8.Length);
                    _buffer.AddRange(utf8);
                    break;
                case BondType.WString:
                    var utf16 = (byte[])value;
                    WriteVarUInt64((ulong)(utf16.Length / 2));
                    _buffer.AddRange(utf16);
                    break;
                case BondType.Struct:
                    WriteStruct((BondStruct)value, depth + 1);
                    break;
                case BondType.List or BondType.Set:
                    WriteContainer(value, depth);
                    break;
                case BondType.Map:
                    var map = (BondMap)value;
                    _buffer.Add((byte)map.KeyType);
                    _buffer.Add((byte)map.ValueType);
                    WriteVarUInt64((ulong)map.Entries.Count);
                    foreach (var (k, v) in map.Entries)
                    {
                        WriteValue(map.KeyType, k, depth + 1);
                        WriteValue(map.ValueType, v, depth + 1);
                    }
                    break;
                default:
                    throw new FormatException($"Unsupported Bond type {(int)type}.");
            }
        }

        private void WriteContainer(object value, int depth)
        {
            if (value is byte[] blob)
            {
                _buffer.Add((byte)BondType.Int8);
                WriteVarUInt64((ulong)blob.Length);
                _buffer.AddRange(blob);
                return;
            }

            var container = (BondContainer)value;
            _buffer.Add((byte)container.ElementType);
            WriteVarUInt64((ulong)container.Items.Count);
            foreach (var item in container.Items) WriteValue(container.ElementType, item, depth + 1);
        }

        private void WriteVarUInt64(ulong value)
        {
            while (value >= 0x80)
            {
                _buffer.Add((byte)(value | 0x80));
                value >>= 7;
            }
            _buffer.Add((byte)value);
        }
    }
}
