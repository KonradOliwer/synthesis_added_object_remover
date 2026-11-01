using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Fields of a record compared and written as one unit. The compare and the copy come from one definition, so what is compared
/// is exactly what is written.
/// </summary>
/// <typeparam name="TVersion">One version of the record as read.</typeparam>
/// <typeparam name="TTarget">What the part is written into.</typeparam>
/// <param name="fields">The record's fields the part holds whole, by their Mutagen names; the rest of the record leaves them out.</param>
/// <param name="differs">Whether two versions hold different values in the part.</param>
/// <param name="copy">Writes the source version's values of the part into the target.</param>
/// <param name="holdsRawBytes">Whether a version holds, in the part, bytes Mutagen does not read.</param>
internal sealed class RecordPart<TVersion, TTarget>(
    IReadOnlyList<string> fields,
    Func<TVersion, TVersion, bool> differs,
    Action<TTarget, TVersion> copy,
    Func<TVersion, bool> holdsRawBytes)
{
    public IReadOnlyList<string> Fields => fields;

    public bool Differs(TVersion one, TVersion other) => differs(one, other);

    /// <summary>Like <see cref="Differs"/>, where a missing version (no record, or no entry) differs from any present one.</summary>
    public bool DiffersOrMissing(TVersion? one, TVersion? other) => (one, other) switch
    {
        ({ } first, { } second) => differs(first, second),
        _ => (one is null) != (other is null),
    };

    public void CopyInto(TTarget target, TVersion source) => copy(target, source);

    public bool HoldsRawBytes(TVersion version) => holdsRawBytes(version);
}

internal static class RawByteFormat
{
    /// <summary>
    /// Mutagen does not read raw byte fields and their layout follows the record's format version, so a part holding them is
    /// written only into a record of the same FormVersion.
    /// </summary>
    public static bool AllowsCopy<TVersion, TTarget>(RecordPart<TVersion, TTarget> part, TVersion source, ISkyrimMajorRecordGetter target)
        where TVersion : ISkyrimMajorRecordGetter =>
        !part.HoldsRawBytes(source) || source.FormVersion == target.FormVersion;
}
