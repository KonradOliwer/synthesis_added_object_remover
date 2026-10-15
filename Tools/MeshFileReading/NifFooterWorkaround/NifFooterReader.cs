// WORKAROUND: the pinned Nifly 1.1.0 package does not read the NIF footer, which lists the file's
// root blocks, so this folder reads it from the raw bytes. REMOVE this folder and use
// NiHeader.RootBlockIds once a NiflySharp/Nifly release that exposes it is adopted.

namespace AddedObjectRemover.NifFooterWorkaround;

/// <param name="BlockCount">Number of blocks the header declares.</param>
/// <param name="RootBlockIndices">Root block indices in footer order, as stored (not range-checked).</param>
internal sealed record NifFooter(int BlockCount, IReadOnlyList<int> RootBlockIndices);

/// <summary>
/// Reads the root block indices from the footer of a Skyrim LE/SE NIF (file version 20.2.0.7, user
/// version 12, Bethesda stream version 83 or 100). The footer follows the last block, so its offset
/// is the end of the header plus the header's block sizes. Field order and conditions follow
/// NiflySharp's NiHeader.Read and NiHeader.ReadFooter.
/// </summary>
internal static class NifFooterReader
{
    private const int HeaderLineMaxLength = 128;
    private const byte HeaderLineEnd = (byte)'\n';
    private const uint SkyrimFileVersion = 0x14020007;
    private const byte LittleEndian = 1;
    private const uint SkyrimUserVersion = 12;
    private const uint SkyrimLeStreamVersion = 83;
    private const uint SkyrimSeStreamVersion = 100;

    /// <summary>Null when the file is not a supported version or its header or footer is inconsistent.</summary>
    public static NifFooter? Read(ReadOnlySpan<byte> data)
    {
        var cursor = new ByteCursor(data);
        if (!TrySkipToBlockCount(ref cursor)
            || !cursor.TryReadInt32(out var blockCount) || blockCount < 0
            || !TrySkipBethesdaStreamHeader(ref cursor)
            || !TrySkipBlockTypes(ref cursor, blockCount)
            || !TryReadTotalBlockSize(ref cursor, blockCount, out var totalBlockSize)
            || !TrySkipStrings(ref cursor)
            || !TrySkipGroups(ref cursor)
            || !cursor.TrySkip(totalBlockSize)
            || !TryReadRoots(ref cursor, blockCount, out var roots))
        {
            return null;
        }
        return new NifFooter(blockCount, roots);
    }

    private static bool TrySkipToBlockCount(ref ByteCursor cursor) =>
        cursor.TrySkipPastByte(HeaderLineEnd, HeaderLineMaxLength + 1)
        && cursor.TryReadUInt32(out var fileVersion) && fileVersion == SkyrimFileVersion
        && cursor.TryReadByte(out var endian) && endian == LittleEndian
        && cursor.TryReadUInt32(out var userVersion) && userVersion == SkyrimUserVersion;

    /// <summary>The creator and two export strings; stream versions above 130 (not Skyrim) add more fields.</summary>
    private static bool TrySkipBethesdaStreamHeader(ref ByteCursor cursor) =>
        cursor.TryReadUInt32(out var streamVersion)
        && streamVersion is SkyrimLeStreamVersion or SkyrimSeStreamVersion
        && TrySkipByteSizedString(ref cursor)
        && TrySkipByteSizedString(ref cursor)
        && TrySkipByteSizedString(ref cursor);

    /// <summary>The block type names and one type index per block.</summary>
    private static bool TrySkipBlockTypes(ref ByteCursor cursor, int blockCount)
    {
        if (!cursor.TryReadUInt16(out var typeCount)) return false;
        for (var i = 0; i < typeCount; i++)
        {
            if (!TrySkipIntSizedString(ref cursor)) return false;
        }
        return cursor.TrySkip((long)blockCount * sizeof(ushort));
    }

    private static bool TryReadTotalBlockSize(ref ByteCursor cursor, int blockCount, out long total)
    {
        total = 0;
        for (var i = 0; i < blockCount; i++)
        {
            if (!cursor.TryReadInt32(out var size) || size < 0) return false;
            total += size;
        }
        return true;
    }

    /// <summary>The string count, the maximum string length and the strings.</summary>
    private static bool TrySkipStrings(ref ByteCursor cursor)
    {
        if (!cursor.TryReadUInt32(out var stringCount) || !cursor.TryReadUInt32(out _)) return false;
        for (var i = 0u; i < stringCount; i++)
        {
            if (!TrySkipIntSizedString(ref cursor)) return false;
        }
        return true;
    }

    private static bool TrySkipGroups(ref ByteCursor cursor) =>
        cursor.TryReadInt32(out var groupCount) && groupCount >= 0
        && cursor.TrySkip((long)groupCount * sizeof(int));

    /// <summary>Every root is a distinct block, so more roots than blocks means the offset or file is wrong.</summary>
    private static bool TryReadRoots(ref ByteCursor cursor, int blockCount, out int[] roots)
    {
        roots = [];
        if (!cursor.TryReadInt32(out var rootCount) || rootCount < 0 || rootCount > blockCount) return false;
        roots = new int[rootCount];
        for (var i = 0; i < rootCount; i++)
        {
            if (!cursor.TryReadInt32(out roots[i])) return false;
        }
        return true;
    }

    private static bool TrySkipByteSizedString(ref ByteCursor cursor) =>
        cursor.TryReadByte(out var length) && cursor.TrySkip(length);

    private static bool TrySkipIntSizedString(ref ByteCursor cursor) =>
        cursor.TryReadInt32(out var length) && cursor.TrySkip(length);
}
