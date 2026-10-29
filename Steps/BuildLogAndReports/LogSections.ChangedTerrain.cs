using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>One finding for each performed settings set, then the objects the round kept; the step's time ends the last finding.</summary>
    /// <param name="found">A performed step's result.</param>
    /// <param name="decisions">The decisions whose last round is the changed-terrain round; they tell the found objects that were restored.</param>
    /// <param name="kept">The objects the changed-terrain round left standing.</param>
    public static LogSection BuildChangedTerrainSection(
        CollectedObjects world,
        PluginName target,
        ChangedTerrainResult found,
        IRemovalDecisions decisions,
        IEnumerable<KeptObject> kept,
        TimeSpan elapsed,
        ReportContext context)
    {
        var performed = found.Passes.Where(pass => pass.Outcome == StepOutcome.Performed).ToList();
        return new("changedTerrain",
        [
            .. performed.SelectMany((pass, position) => PassLines(world, target, pass, decisions, context, position == performed.Count - 1 ? elapsed : null)),
            .. BuildKeptLines(world, kept),
        ]);
    }

    /// <param name="elapsed">The step's time to print after the finding; null for none.</param>
    private static IEnumerable<string> PassLines(
        CollectedObjects world, PluginName target, ChangedTerrainPass pass, IRemovalDecisions decisions, ReportContext context, TimeSpan? elapsed)
    {
        var restored = RemovalList.CountRestored(decisions, pass.Hits.Select(hit => hit.Target));
        yield return $"Found {TextFormat.Count(pass.Hits.Length)} of {DescribeJudgedObjects(TargetOrigins.At(world, pass.Objects.Select(id => id.Index)), target)} on terrain changed by other mods"
            + $"{(elapsed is { } time ? Describe.TimedSuffix(time, context) : string.Empty)}{DescribeRestoredOfFound(restored)}.";
        foreach (var line in ChangedTerrainCounts(pass, context)) yield return line;
    }

    private static ImmutableArray<string> ChangedTerrainCounts(ChangedTerrainPass found, ReportContext context)
    {
        if (!context.Detailed) return [];

        var changers = found.Hits.Select(hit => Describe.ChangingMod(hit.ChangedBy));
        var byChanger = KeyedGroups.Rank(KeyedGroups.CountBy(changers, name => name, StringComparer.Ordinal), StringComparer.Ordinal);
        return
        [
            $"  Terrain: {TextFormat.Count(found.CellsCompared)} cells compared; {TextFormat.Count(found.Hits.Length)} objects on changed terrain "
                + $"(by changing mod: {TextLists.JoinOr(TextLists.Counts(byChanger), Describe.ListSeparator, Describe.NoEntries)}); "
                + $"{TextFormat.Count(found.ObjectsWithoutTerrain)} objects not checked (interior, no terrain, or no trusted land record).",
        ];
    }
}
