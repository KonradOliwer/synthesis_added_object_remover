using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Boundary around Mutagen reads of the user's plugins: what Mutagen raises for malformed plugin data becomes
/// <see cref="MalformedRecordException"/>, so the same exception types thrown by this project's own code still surface as bugs.
/// </summary>
internal static class MutagenCalls
{
    private static readonly Assembly Runtime = typeof(object).Assembly;

    /// <summary>Mutagen's record parsers and the byte reader under them.</summary>
    private static readonly ImmutableArray<Assembly> Parsers =
        [typeof(MalformedDataException).Assembly, typeof(SkyrimMod).Assembly, typeof(ReadOnlyMemorySlice<>).Assembly];

    public static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsMalformedData(ex))
        {
            throw new MalformedRecordException(ex);
        }
    }

    /// <remarks>
    /// Mutagen's own types: <see cref="MalformedDataException"/> (a check of the data failed), <see cref="RecordException"/> and
    /// its <see cref="SubrecordException"/> (a parse failure wrapped with the record's context while records are enumerated or
    /// copied), and <see cref="ModGroupsMalformedException"/> (a plugin's groups do not add up when it is opened). The lazy record
    /// views parse a subrecord when it is first asked for and wrap nothing: a cut or overlong subrecord or count gives
    /// <see cref="ArgumentOutOfRangeException"/> or <see cref="IndexOutOfRangeException"/> from the byte reader, an unknown
    /// script property type or object format <see cref="NotImplementedException"/>. Those three are also bugs of ours, so they
    /// count only when a parser method threw them, past the runtime's own throw helpers.
    /// </remarks>
    public static bool IsMalformedData(Exception ex) =>
        ex is MalformedDataException or RecordException or ModGroupsMalformedException
        || ((ex is ArgumentOutOfRangeException or IndexOutOfRangeException or NotImplementedException) && WasThrownByAParser(ex));

    private static bool WasThrownByAParser(Exception ex) =>
        new StackTrace(ex).GetFrames()
            .Select(frame => frame.GetMethod()?.DeclaringType?.Assembly)
            .FirstOrDefault(assembly => assembly != Runtime) is { } thrower
        && Parsers.Contains(thrower);
}
