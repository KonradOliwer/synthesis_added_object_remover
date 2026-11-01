using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>What the target plugin last changed in a navmesh record, part by part, judged on the records as their plugins store them.</summary>
internal static class NavmeshPartHistory
{
    /// <param name="read">For a navmesh the target plugin defines, the version below its own holds no record.</param>
    /// <param name="patch">The patch, whose version holds what earlier patchers of the group wrote.</param>
    /// <remarks>A geometry whose earlier bytes Mutagen does not read cannot be written into a winner of another record format, so it stays.</remarks>
    public static (PartOutcome Geometry, PartOutcome Presence) Of(RecordVersions<NavmeshVersion?> read, PluginName patch) =>
        (OutcomeOf(NavmeshRecordParts.Geometry, read, patch), OutcomeOf(NavmeshRecordParts.Presence, read, patch));

    private static PartOutcome OutcomeOf(RecordPart<INavigationMeshGetter, INavigationMesh> part, RecordVersions<NavmeshVersion?> read, PluginName patch)
    {
        var versions = read.Versions;
        var outcome = PartHistory.Of(read.Plugins, PartHistory.TargetVersionIndex, patch, (lower, higher) => part.DiffersOrMissing(versions[lower]?.Record, versions[higher]?.Record));
        return outcome is PartOutcome.Undo && read.Earlier is { } earlier && !RawByteFormat.AllowsCopy(part, earlier.Record, read.Winner!.Record)
            ? new PartOutcome.KeptForRecordFormat()
            : outcome;
    }
}
