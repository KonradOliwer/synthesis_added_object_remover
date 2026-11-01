using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>What the target plugin last changed in a placed record of another plugin it overrides, part by part, judged on the records as their plugins store them.</summary>
internal static class PlacedPartHistory
{
    /// <param name="versions">Lowest first: the version just below the target plugin's, the target plugin's, then each later one up to the winner.</param>
    /// <param name="plugins">The plugin of each version.</param>
    /// <param name="patch">The patch, whose version holds what earlier patchers of the group wrote.</param>
    /// <remarks>
    /// A door's navmesh link goes back only together with its navmesh, so it is judged with the navmeshes, not here. A part whose
    /// earlier bytes Mutagen does not read cannot be written into a winner of another record format, so it stays.
    /// </remarks>
    public static PlacedPartChanges Of(PlacedRecordParts table, IReadOnlyList<IPlacedGetter> versions, IReadOnlyList<PluginName> plugins, PluginName patch) =>
        PlacedPartJudging.Judge(
            table.Parts
                .Where(part => part.Part != PlacedParts.NavDoorLink)
                .Select(part => new PlacedPartCompare<IPlacedGetter>(
                    part.Part, part.Compare.Differs, (source, winner) => RawByteFormat.AllowsCopy(part.Compare, source, winner))),
            versions,
            plugins,
            patch);
}
