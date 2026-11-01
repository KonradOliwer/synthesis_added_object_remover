using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The parts of a navmesh record and of its entry in the navmesh info map, each with its compare and its copy.</summary>
internal static class NavmeshRecordParts
{
    /// <summary>Never compared, never written.</summary>
    public const int ExcludedFlagBits = RecordHeader.CompressedBit;

    /// <summary>The record flags the geometry holds: all but Deleted and the excluded ones.</summary>
    private const int GeometryFlagBits = ~(RecordHeader.DeletedBit | ExcludedFlagBits);

    /// <summary>Never compared, never written.</summary>
    public static IReadOnlyList<string> ExcludedFields => RecordHeader.Fields;

    private static readonly NavigationMesh.TranslationMask GeometryMask =
        TranslationMasks.AllBut<NavigationMesh.TranslationMask>([.. ExcludedFields, .. RecordHeader.FlagFields]);

    /// <summary>
    /// The whole <see cref="INavigationMeshGetter.Data"/> (vertices, triangles, edge links, door triangles, grid, parent, bounds)
    /// with NNAM, ONAM and PNAM, and every other field and record flag but Deleted.
    /// </summary>
    public static RecordPart<INavigationMeshGetter, INavigationMesh> Geometry { get; } = new(
        [],
        static (one, other) => FlagBits.Differ(one.MajorRecordFlagsRaw, other.MajorRecordFlagsRaw, GeometryFlagBits) || !one.Equals(other, GeometryMask),
        static (target, source) =>
        {
            target.MajorRecordFlagsRaw = FlagBits.CopyBits(target.MajorRecordFlagsRaw, source.MajorRecordFlagsRaw, GeometryFlagBits);
            ((INavigationMeshInternal)target).DeepCopyIn(source, errorMask: null, copyMask: GeometryMask.GetCrystal());
        },
        static version => version.NNAM is not null || version.ONAM is not null || version.PNAM is not null || version.Data is { NavmeshGrid.Length: > 0 });

    /// <summary>The Deleted flag.</summary>
    public static RecordPart<INavigationMeshGetter, INavigationMesh> Presence { get; } = new(
        [],
        static (one, other) => FlagBits.Differ(one.MajorRecordFlagsRaw, other.MajorRecordFlagsRaw, RecordHeader.DeletedBit),
        static (target, source) => target.MajorRecordFlagsRaw = FlagBits.CopyBits(target.MajorRecordFlagsRaw, source.MajorRecordFlagsRaw, RecordHeader.DeletedBit),
        static _ => false);

    /// <summary>A navmesh's whole entry in the navmesh info map; the copy replaces the target map's entry of the same navmesh.</summary>
    public static RecordPart<INavigationMapInfoGetter, INavigationMeshInfoMap> MapEntry { get; } = new(
        [],
        static (one, other) => !Equals(one, other),
        static (target, source) =>
        {
            target.MapInfos.RemoveAll(entry => entry.NavigationMesh.FormKey == source.NavigationMesh.FormKey);
            target.MapInfos.Add(source.DeepCopy());
        },
        static _ => false);
}
