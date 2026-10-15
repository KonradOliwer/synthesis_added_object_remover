using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <param name="found">A performed step's result.</param>
    /// <param name="kept">The objects the changed-terrain round left standing.</param>
    public static LogSection BuildChangedTerrainSection(
        CollectedObjects world,
        PluginName target,
        ChangedTerrainResult found,
        IEnumerable<KeptObject> kept,
        TimeSpan elapsed,
        ReportContext context) =>
        new("changedTerrain",
        [
            $"Found {TextFormat.Count(found.Hits.Length)} of {TextFormat.Count(world.Targets.Length)} {target} objects on terrain changed by other mods"
                + $"{Describe.TimedSuffix(elapsed, context)}.",
            .. ChangedTerrainCounts(found, context),
            .. BuildKeptLines(world, kept),
        ]);

    private static ImmutableArray<string> ChangedTerrainCounts(ChangedTerrainResult found, ReportContext context)
    {
        if (!context.Detailed) return [];

        var changers = found.Hits.Select(hit => hit.ChangedBy.ToString());
        var byPlugin = KeyedGroups.Rank(KeyedGroups.CountBy(changers, name => name, StringComparer.Ordinal), StringComparer.Ordinal);
        return
        [
            $"  Terrain: {TextFormat.Count(found.CellsCompared)} cells compared; {TextFormat.Count(found.Hits.Length)} objects on changed terrain "
                + $"(by plugin: {TextLists.JoinOr(TextLists.Counts(byPlugin), Describe.ListSeparator, Describe.NoEntries)}); "
                + $"{TextFormat.Count(found.ObjectsWithoutTerrain)} objects not checked (interior or no terrain).",
        ];
    }
}
