using Mutagen.Bethesda.Plugins.Records;

namespace AddedObjectRemover;

/// <summary>Compares and copies chosen bits of a flags value.</summary>
internal static class FlagBits
{
    public static bool Differ(int one, int other, int bits) => ((one ^ other) & bits) != 0;

    /// <returns>The target with the chosen bits taken from the source.</returns>
    public static int CopyBits(int target, int source, int bits) => (target & ~bits) | (source & bits);

    /// <summary>Whether the chosen bits of the records' flags differ.</summary>
    public static bool DifferIn(IMajorRecordGetter one, IMajorRecordGetter other, int bits) =>
        Differ(one.MajorRecordFlagsRaw, other.MajorRecordFlagsRaw, bits);

    /// <summary>Sets the chosen bits of the target record's flags to those of the source record's.</summary>
    public static void CopyMajorBits(IMajorRecord target, IMajorRecordGetter source, int bits) =>
        target.MajorRecordFlagsRaw = CopyBits(target.MajorRecordFlagsRaw, source.MajorRecordFlagsRaw, bits);
}
