using System.Buffers.Binary;

namespace AddedObjectRemover.NifFooterWorkaround;

/// <summary>Forward-only little-endian reader over raw bytes whose reads fail instead of throwing when the data ends.</summary>
internal ref struct ByteCursor(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Position { get; private set; }

    public bool TrySkip(long count)
    {
        if (count < 0 || count > _data.Length - Position) return false;
        Position += (int)count;
        return true;
    }

    public bool TrySkipPastByte(byte value, int maxCount)
    {
        var searched = _data[Position..Math.Min(_data.Length, Position + maxCount)];
        var found = searched.IndexOf(value);
        return found >= 0 && TrySkip(found + 1);
    }

    public bool TryReadByte(out byte value)
    {
        value = Position < _data.Length ? _data[Position] : default;
        return TrySkip(sizeof(byte));
    }

    public bool TryReadUInt16(out ushort value)
    {
        var read = BinaryPrimitives.TryReadUInt16LittleEndian(_data[Position..], out value);
        return read && TrySkip(sizeof(ushort));
    }

    public bool TryReadUInt32(out uint value)
    {
        var read = BinaryPrimitives.TryReadUInt32LittleEndian(_data[Position..], out value);
        return read && TrySkip(sizeof(uint));
    }

    public bool TryReadInt32(out int value)
    {
        var read = BinaryPrimitives.TryReadInt32LittleEndian(_data[Position..], out value);
        return read && TrySkip(sizeof(int));
    }
}
