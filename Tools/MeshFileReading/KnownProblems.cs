using ICSharpCode.SharpZipLib;
using IniParser.Exceptions;
using Mutagen.Bethesda.Archives.Exceptions;

namespace AddedObjectRemover;

/// <summary>Sorts exceptions into known user-data problems; where they are caught, each becomes a recorded problem of its own kind, which the caller words.</summary>
internal static class KnownProblems
{
    /// <summary>
    /// What NiflySharp raises for truncated or corrupt NIFs. Its reader trusts stored block
    /// references, so garbage input also surfaces as NullReference; only valid as a filter around
    /// NiflySharp calls (see <see cref="NiflyCalls"/>).
    /// </summary>
    public static bool IsMalformedNif(Exception ex) =>
        ex is IOException
            or ArgumentException
            or IndexOutOfRangeException
            or InvalidCastException
            or InvalidOperationException
            or NotSupportedException
            or NotImplementedException
            or ArithmeticException
            or FormatException
            or KeyNotFoundException
            or NullReferenceException;

    /// <summary>
    /// What Mutagen's archive readers and their zlib/LZ4 decoders raise for unreadable or corrupt BSAs, by exact type where the
    /// base type is wider than the fault: a file that cannot be opened or ends early (<see cref="IOException"/>), a header or
    /// directory that does not add up (<see cref="ArchiveException"/>, <see cref="OverflowException"/> from its checked sizes, and
    /// the byte readers' <see cref="ArgumentOutOfRangeException"/> and <see cref="IndexOutOfRangeException"/> for a cut directory),
    /// and a data block the decoder rejects (<see cref="SharpZipBaseException"/> for zlib, <see cref="InvalidOperationException"/> for LZ4).
    /// Only valid as a filter around the archive library's calls and the read loop of the stream they return.
    /// </summary>
    public static bool IsCorruptArchive(Exception ex) =>
        FileFailures.IsExpected(ex)
            || ex is ArchiveException or OverflowException or SharpZipBaseException
            || ex.GetType() == typeof(InvalidOperationException)
            || ex.GetType() == typeof(ArgumentOutOfRangeException)
            || ex.GetType() == typeof(IndexOutOfRangeException);

    /// <summary>What reading the game INI's archive list raises (missing or unreadable INI, unknown game).</summary>
    public static bool IsUnreadableIni(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotImplementedException
            or ParsingException;
}
