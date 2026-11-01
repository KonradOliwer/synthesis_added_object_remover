using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>How the parts of a placed record differ across its versions, compared on the records as their plugins store them.</summary>
internal static class PlacedPartHistory
{
    /// <param name="versions">Lowest first: the version just below the target plugin's, the target plugin's, then each later one up to the winner.</param>
    /// <param name="plugins">The plugin of each version.</param>
    public static PlacedPartDifferences Of(PlacedRecordParts table, IReadOnlyList<IPlacedGetter> versions, IReadOnlyList<PluginName> plugins) =>
        PlacedPartComparison.Compare(
            [
                .. table.Parts.Select(part => new PlacedPartCompare<IPlacedGetter>(
                    part.Part, part.Compare.Differs, (source, winner) => RawByteFormat.AllowsCopy(part.Compare, source, winner))),
            ],
            versions,
            plugins);
}
