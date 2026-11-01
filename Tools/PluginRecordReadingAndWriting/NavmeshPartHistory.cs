using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>How the parts of a navmesh record differ across its versions, compared on the records as their plugins store them.</summary>
internal static class NavmeshPartHistory
{
    /// <param name="read">For a navmesh the target plugin defines, the version below its own holds no record.</param>
    public static (PartDifferences Geometry, PartDifferences Presence) Of(RecordVersions<NavmeshVersion?> read) =>
        (DifferencesOf(NavmeshRecordParts.Geometry, read), DifferencesOf(NavmeshRecordParts.Presence, read));

    private static PartDifferences DifferencesOf(RecordPart<INavigationMeshGetter, INavigationMesh> part, RecordVersions<NavmeshVersion?> read)
    {
        var versions = read.Versions;
        return VersionComparison.ComparePart(
            read.Plugins,
            (lower, higher) => part.DiffersOrMissing(versions[lower]?.Record, versions[higher]?.Record),
            read.Earlier is { } earlier && !RawByteFormat.AllowsCopy(part, earlier.Record, read.Winner!.Record));
    }
}
