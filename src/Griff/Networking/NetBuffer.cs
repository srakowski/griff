using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Griff.Networking;

/// <summary>Builds a network message: numbers little-endian, counts and lengths as variable-length integers.</summary>
public sealed class NetWriter
{
    private byte[] _buffer;

    public NetWriter(int capacity = 256) => _buffer = new byte[Math.Max(16, capacity)];

    public int Length { get; private set; }

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, Length);

    public byte[] ToArray() => _buffer.AsSpan(0, Length).ToArray();

    public void Clear() => Length = 0;

    public void Byte(byte value)
    {
        Ensure(1);
        _buffer[Length++] = value;
    }

    public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    public void UShort(ushort value)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(Length), value);
        Length += 2;
    }

    public void UInt(uint value)
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(Length), value);
        Length += 4;
    }

    public void Int(int value)
    {
        Ensure(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(Length), value);
        Length += 4;
    }

    public void Float(float value)
    {
        Ensure(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(Length), value);
        Length += 4;
    }

    public void Double(double value)
    {
        Ensure(8);
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer.AsSpan(Length), value);
        Length += 8;
    }

    /// <summary>A non-negative number in 1 to 5 bytes: small numbers (counts, ids) take one.</summary>
    public void VarUInt(uint value)
    {
        Ensure(5);
        while (value >= 0x80)
        {
            _buffer[Length++] = (byte)(value | 0x80);
            value >>= 7;
        }

        _buffer[Length++] = (byte)value;
    }

    /// <summary>Any int, small ones of either sign in one byte.</summary>
    public void VarInt(int value) => VarUInt((uint)((value << 1) ^ (value >> 31)));

    public void String(string? value)
    {
        if (value is null)
        {
            VarUInt(0);
            return;
        }

        var count = Encoding.UTF8.GetByteCount(value);
        VarUInt((uint)count + 1);
        Ensure(count);
        Length += Encoding.UTF8.GetBytes(value, _buffer.AsSpan(Length));
    }

    public void Guid(Guid value)
    {
        Ensure(16);
        value.TryWriteBytes(_buffer.AsSpan(Length));
        Length += 16;
    }

    public void Vector2(Vector2 value)
    {
        Float(value.X);
        Float(value.Y);
    }

    public void Vector3(Vector3 value)
    {
        Float(value.X);
        Float(value.Y);
        Float(value.Z);
    }

    public void Bytes(ReadOnlySpan<byte> value)
    {
        VarUInt((uint)value.Length);
        Raw(value);
    }

    /// <summary>Bytes with no length in front, e.g. a message already framed.</summary>
    public void Raw(ReadOnlySpan<byte> value)
    {
        Ensure(value.Length);
        value.CopyTo(_buffer.AsSpan(Length));
        Length += value.Length;
    }

    private void Ensure(int more)
    {
        if (Length + more <= _buffer.Length)
            return;
        Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + more));
    }
}

/// <summary>Reads what a <see cref="NetWriter"/> wrote. Running off the end throws <see cref="NetFormatException"/>.</summary>
public ref struct NetReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Position { get; private set; }

    public readonly int Remaining => _data.Length - Position;

    public readonly bool AtEnd => Position >= _data.Length;

    public byte Byte() => Take(1)[0];

    public bool Bool() => Byte() != 0;

    public ushort UShort() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public float Float() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public double Double() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public uint VarUInt()
    {
        uint value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = Byte();
            value |= (uint)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }

        throw new NetFormatException("A number in the message is too long.");
    }

    public int VarInt()
    {
        var raw = VarUInt();
        return (int)(raw >> 1) ^ -(int)(raw & 1);
    }

    public string? String()
    {
        var count = VarUInt();
        if (count == 0)
            return null;
        return Encoding.UTF8.GetString(Take(checked((int)count - 1)));
    }

    public Guid Guid() => new(Take(16));

    public Vector2 Vector2() => new(Float(), Float());

    public Vector3 Vector3() => new(Float(), Float(), Float());

    public ReadOnlySpan<byte> Bytes() => Take(checked((int)VarUInt()));

    public ReadOnlySpan<byte> Rest() => Take(Remaining);

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
            throw new NetFormatException("The message ended early.");
        var span = _data.Slice(Position, count);
        Position += count;
        return span;
    }
}

/// <summary>A message that doesn't read the way it should: cut short, or from a different version of the game.</summary>
public sealed class NetFormatException(string message) : Exception(message);
