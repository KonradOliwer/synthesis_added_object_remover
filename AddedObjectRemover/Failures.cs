using ICSharpCode.SharpZipLib;
using IniParser.Exceptions;

namespace AddedObjectRemover;

/// <summary>
/// Sorts failures into known user-data problems, which have their own specific wording where they are caught,
/// and unexpected ones, which are caught only where one independent part of the run is processed.
/// </summary>
internal static class Failures
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

    /// <summary>
    /// Whether the run may carry on without the part that threw. OutOfMemory is deliberately excluded: it cannot be
    /// told apart from real memory pressure, so skipping a part would make the results depend on memory.
    /// That includes one wrapped by parallel work.
    /// </summary>
    public static bool IsRecoverable(Exception ex) =>
        ex is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.All(IsRecoverable)
            : ex is not OutOfMemoryException;

    public static UnexpectedError Unexpected(string part, string consequence, Exception ex) =>
        new(part, Describe(ex), consequence);

    public static UnexpectedError Unexpected(string part, string consequence, Exception ex, ErrorSubject subject) =>
        new(part, Describe(ex), consequence, subject);

    /// <remarks>Parallel work wraps its failures; the distinct inner ones are listed in a fixed order, so the text does not depend on which worker failed first.</remarks>
    public static string Describe(Exception ex) =>
        ex is AggregateException aggregate
            ? string.Join("; ", aggregate.Flatten().InnerExceptions.Select(Describe).Distinct().Order(StringComparer.Ordinal))
            : $"{ex.GetType().Name}: {string.Join(' ', ex.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.')}";

    /// <summary>Runs one part of the run; if it throws unexpectedly, reports that and gives the well-defined result of a part that did not run.</summary>
    /// <param name="consequence">What is missing or incomplete in the results when the part did not run.</param>
    /// <param name="notRun">Builds the result of the part not having run; only called when the part threw.</param>
    public static T Guard<T>(Action<UnexpectedError> report, string part, string consequence, Func<T> work, Func<T> notRun)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            report(Unexpected(part, consequence, ex));
            return notRun();
        }
    }
}
