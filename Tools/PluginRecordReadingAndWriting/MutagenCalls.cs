using Mutagen.Bethesda.Plugins.Exceptions;

namespace AddedObjectRemover;

/// <summary>
/// Boundary around the Mutagen calls that read the user's plugin bytes: what they raise for malformed data becomes
/// <see cref="MalformedRecordException"/>. Everything thrown outside it is a bug and ends the run.
/// </summary>
/// <remarks>
/// The call handed in is a Mutagen getter or a Mutagen copy that reads its source record (<c>DeepCopy</c>, <c>DeepCopyIn</c>,
/// <c>GetOrAddAsOverride</c>), with the runtime's own collection and LINQ calls over what they return: none of this project's code
/// and no indexing of a Mutagen or Noggog collection by an index of ours. A copy only assigns values it has parsed from its source
/// into the target, so whatever is thrown inside comes from parsing the source bytes, and the exception's type alone tells
/// malformed data, in every build and at every JIT tier.
/// A record's header fields (form key, flags, deletion) were checked when the record was found, so their getters need no wrapper.
/// </remarks>
internal static class MutagenCalls
{
    public static T Read<T>(Func<T> getters)
    {
        try
        {
            return getters();
        }
        catch (Exception ex) when (IsMalformedData(ex))
        {
            throw new MalformedRecordException(ex);
        }
    }

    /// <param name="copy">A Mutagen copy that returns nothing; it parses the bytes of the source record and only assigns what it parsed into the target.</param>
    public static void Copy(Action copy) => Read<object?>(() =>
    {
        copy();
        return null;
    });

    /// <remarks>
    /// Mutagen's own types: <see cref="MalformedDataException"/> (a check of the data failed) and
    /// <see cref="ModGroupsMalformedException"/> (a plugin's groups do not add up), and <see cref="RecordException"/> with its
    /// <see cref="SubrecordException"/>, which only add the record's context to what they wrap and count as what they wrap. The lazy
    /// record views parse a subrecord when it is first asked for and wrap nothing: a cut or overlong subrecord or count gives
    /// <see cref="ArgumentOutOfRangeException"/> or <see cref="IndexOutOfRangeException"/> from the byte reader, an unknown script
    /// property type or object format <see cref="NotImplementedException"/>.
    /// </remarks>
    private static bool IsMalformedData(Exception ex) => ex switch
    {
        AggregateException aggregate => aggregate.Flatten().InnerExceptions is { Count: > 0 } inner && inner.All(IsMalformedData),
        RecordException { InnerException: { } inner } => IsMalformedData(inner),
        RecordException or MalformedDataException or ModGroupsMalformedException => true,
        _ => ex.GetType() == typeof(ArgumentOutOfRangeException) || ex.GetType() == typeof(IndexOutOfRangeException) || ex.GetType() == typeof(NotImplementedException),
    };
}
