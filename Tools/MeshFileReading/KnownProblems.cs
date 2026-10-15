using ICSharpCode.SharpZipLib;
using IniParser.Exceptions;

namespace AddedObjectRemover;

/// <summary>Sorts exceptions into known user-data problems; where they are caught, each becomes a recorded problem of its own kind, which the report step words.</summary>
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

    /// <summary>What Mutagen's archive readers and their zlib/LZ4 decoders raise for unreadable or corrupt BSAs.</summary>
    public static bool IsCorruptArchive(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or IndexOutOfRangeException
            or InvalidOperationException
            or NotSupportedException
            or NotImplementedException
            or ArithmeticException
            or SharpZipBaseException;

    /// <summary>What reading the game INI's archive list raises (missing or unreadable INI, unknown game).</summary>
    public static bool IsUnreadableIni(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotImplementedException
            or ParsingException;
}
