using System.Text;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Just enough protobuf for SABR. Every field in the SABR schema is an optional scalar or a
/// length-delimited message — no oneofs, maps, groups or packed repeats — so hand-rolling the codec
/// avoids a protoc/Google.Protobuf dependency for the ~13 messages we actually speak.
/// </summary>
internal sealed class ProtoWriter
{
    private readonly MemoryStream _buffer = new();

    public byte[] ToArray() => _buffer.ToArray();

    public void Varint(int field, long? value)
    {
        if (value is null) return;
        Tag(field, 0);
        // Cast through ulong so a negative value sign-extends to the 10-byte form protobuf expects.
        WriteVarint((ulong)value.Value);
    }

    public void Varint(int field, ulong? value)
    {
        if (value is null) return;
        Tag(field, 0);
        WriteVarint(value.Value);
    }

    public void Bool(int field, bool? value)
    {
        if (value is null) return;
        Tag(field, 0);
        WriteVarint(value.Value ? 1UL : 0UL);
    }

    public void String(int field, string? value)
    {
        if (value is null) return;
        Bytes(field, Encoding.UTF8.GetBytes(value));
    }

    public void Bytes(int field, byte[]? value)
    {
        if (value is null) return;
        Tag(field, 2);
        WriteVarint((ulong)value.Length);
        _buffer.Write(value);
    }

    public void Message(int field, IProtoMessage? message)
    {
        if (message is null) return;
        var nested = new ProtoWriter();
        message.WriteTo(nested);
        Bytes(field, nested.ToArray());
    }

    public void Messages(int field, IEnumerable<IProtoMessage> messages)
    {
        foreach (var message in messages)
            Message(field, message);
    }

    private void Tag(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _buffer.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        _buffer.WriteByte((byte)value);
    }
}

internal interface IProtoMessage
{
    void WriteTo(ProtoWriter writer);
}

internal ref struct ProtoReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position = 0;

    /// <summary>Advances to the next field. Returns false at the end of the message.</summary>
    public bool Next(out int field, out int wireType)
    {
        if (_position >= _data.Length)
        {
            field = 0;
            wireType = 0;
            return false;
        }
        var tag = ReadVarint();
        field = (int)(tag >> 3);
        wireType = (int)(tag & 0x7);
        return true;
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            if (_position >= _data.Length)
                throw new InvalidDataException("Truncated protobuf varint");
            var b = _data[_position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
            if (shift > 63)
                throw new InvalidDataException("Protobuf varint too long");
        }
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        var length = (int)ReadVarint();
        if (length < 0 || _position + length > _data.Length)
            throw new InvalidDataException("Truncated protobuf length-delimited field");
        var slice = _data.Slice(_position, length);
        _position += length;
        return slice;
    }

    public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

    /// <summary>Skips a field we don't model. Unknown fields are normal here — the schema is reverse-engineered.</summary>
    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case 0: ReadVarint(); break;
            case 1: _position += 8; break;
            case 2: ReadBytes(); break;
            case 5: _position += 4; break;
            default: throw new InvalidDataException($"Unsupported protobuf wire type {wireType}");
        }
        if (_position > _data.Length)
            throw new InvalidDataException("Truncated protobuf field");
    }
}
