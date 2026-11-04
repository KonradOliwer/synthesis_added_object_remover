using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>How the parts of a LAND record differ across its versions, compared on the records as their plugins store them.</summary>
internal static class LandPartComparison
{
    /// <param name="cellDeletedInAVersion">A version read of the cell holding the LAND record deletes the cell.</param>
    public static LandRecordFact Of(LandRecordParts table, RecordKey key, ExteriorCell cell, RecordVersions<ILandscapeGetter> read, bool cellDeletedInAVersion)
    {
        var points = read.Versions.Select(LandPoints.Read).ToList();
        var versions = VersionComparison.Compare(
            read.Plugins,
            (lower, higher) => new LandParts(
                PointSet.Of(DifferingIndices(table.Points, points[lower], points[higher])),
                PointSet.Of(DifferingIndices(table.Colours, points[lower], points[higher])),
                QuarterSet.Of(DifferingIndices(table.Quarters, read.Versions[lower], read.Versions[higher])),
                table.Rest.Differs(read.Versions[lower], read.Versions[higher])),
            new LandParts(PointSet.Of([]), PointSet.Of([]), default, Rest: false));
        var namedPluginChangedNoPoint = VersionComparison.NamedPluginVersionOf(versions).FromBelow.Points.IsEmpty;
        return new LandRecordFact(
            key,
            cell,
            read.WinningMod,
            versions,
            read.Versions.Any(version => version.IsDeleted),
            HeightsToKeep(namedPluginChangedNoPoint,points[0].Heights, points[^1].Heights),
            cellDeletedInAVersion,
            [.. points.Skip(1).SkipLast(1).Select(version => version.Heights)]);
    }

    /// <returns>Null when the named plugin changed no height or normal and both versions hold the same heights; they are then read when needed.</returns>
    private static LandHeights? HeightsToKeep(bool namedPluginChangedNoPoint, float[]? earlier, float[]? current) =>
        namedPluginChangedNoPoint && SameHeights(earlier, current) ? null : new LandHeights(earlier, current);

    private static bool SameHeights(float[]? earlier, float[]? current) =>
        (earlier, current) switch
        {
            (null, null) => true,
            ({ } one, { } other) => one.AsSpan().SequenceEqual(other),
            _ => false,
        };

    private static IEnumerable<int> DifferingIndices<TVersion, TTarget>(IReadOnlyList<RecordPart<TVersion, TTarget>> parts, TVersion lower, TVersion higher) =>
        parts.Index().Where(entry => entry.Item.Differs(lower, higher)).Select(entry => entry.Index);
}
