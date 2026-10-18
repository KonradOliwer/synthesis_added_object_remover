using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>How many areas of each level the normal log names before it folds the rest into one count.</summary>
    private const int TopAreaCount = 5;

    private static readonly AreaLevelName[] AreaLevels =
        [new(AreaLevel.Worldspace, "worldspaces"), new(AreaLevel.Hold, "holds"), new(AreaLevel.Place, "places")];

    /// <summary>
    /// Where the objects of each closing summary are, by worldspace, hold and place: the removals, the kept objects, the
    /// objects to check for a manual patch and each removal warning; only summaries with objects have lines. The normal log
    /// names the top areas of each level; the detailed log lists every area, and for the removals per hold also how many
    /// target objects are there.
    /// </summary>
    public static LogSection BuildAreasSection(
        CollectedObjects world,
        IReadOnlyList<RemovedObject> removals,
        IEnumerable<KeptObject> kept,
        IEnumerable<ManualPatchHint> hints,
        RemovalRisks risks,
        ReportContext context)
    {
        List<int> removedTargets = [.. removals.Select(removal => removal.TargetIndex)];
        var removed = removedTargets.ToHashSet();
        var keptOnce = kept.Select(entry => entry.TargetIndex).Distinct().ToList();
        var hintTargets = hints.Select(hint => hint.TargetIndex).Distinct().ToList();
        var uniqueActors = RemovedUniqueActors(risks, removed).Select(role => role.Target.Index).Distinct().ToList();
        var largeReferences = RemovedLargeReferences(risks, removed).ToList();
        var targetsPerHold = context.Detailed ? CountTargetsPerHold(world) : null;
        return new LogSection(
            "areas",
            [
                .. AreaLines(world, removedTargets, $"Removed {TextFormat.Count(removedTargets.Count)} objects in:", targetsPerHold, context),
                .. AreaLines(world, keptOnce, $"Kept {TextFormat.Count(keptOnce.Count)} objects that would have been removed, in:", targetsPerHold: null, context),
                .. AreaLines(world, hintTargets, $"Possible manual patch needed for {TextFormat.Count(hintTargets.Count)} objects, in:", targetsPerHold: null, context),
                .. AreaLines(world, uniqueActors, $"Removed {TextFormat.Count(uniqueActors.Count)} unique NPCs, in:", targetsPerHold: null, context),
                .. AreaLines(world, largeReferences, $"Removed {TextFormat.Count(largeReferences.Count)} objects of the large-reference lists, in:", targetsPerHold: null, context),
            ]);
    }

    private static Dictionary<AreaGroup, int> CountTargetsPerHold(CollectedObjects world) =>
        AreaGroups.Rank(world, Enumerable.Range(0, world.Targets.Length), AreaLevel.Hold).ToDictionary(entry => entry.Key, entry => entry.Value);

    /// <param name="targetsPerHold">With it, each hold of the detailed log also shows how many target objects are there.</param>
    private static IEnumerable<string> AreaLines(
        CollectedObjects world, IReadOnlyList<int> targets, string header, IReadOnlyDictionary<AreaGroup, int>? targetsPerHold, ReportContext context)
    {
        if (targets.Count == 0) return [];
        return [header, .. AreaLevels.SelectMany(level => LevelLines(world, targets, level, level.Level == AreaLevel.Hold ? targetsPerHold : null, context))];
    }

    private static IEnumerable<string> LevelLines(
        CollectedObjects world, IReadOnlyList<int> targets, AreaLevelName level, IReadOnlyDictionary<AreaGroup, int>? targetsThere, ReportContext context)
    {
        var ranked = AreaGroups.Rank(world, targets, level.Level);
        return context.Detailed ? AllAreasLines(ranked, level.Plural, targetsThere) : [TopAreasLine(ranked, level.Plural)];
    }

    /// <summary>E.g. "  places: 41 Loreius Farm (Skyrim), 30 Dawnstar (Skyrim); 12 more in 3 other places."</summary>
    private static string TopAreasLine(IReadOnlyList<KeyValuePair<AreaGroup, int>> ranked, string plural)
    {
        var shown = TextLists.TopN(ranked, TopAreaCount, entry => $"{TextFormat.Count(entry.Value)} {entry.Key.Name}", Describe.ListSeparator);
        var folded = ranked.Skip(TopAreaCount).ToList();
        var more = folded.Count == 0
            ? string.Empty
            : $"; {TextFormat.Count(folded.Sum(entry => entry.Value))} more in {TextFormat.Count(folded.Count)} other {plural}";
        return $"  {plural}: {shown}{more}.";
    }

    private static IEnumerable<string> AllAreasLines(
        IReadOnlyList<KeyValuePair<AreaGroup, int>> ranked, string plural, IReadOnlyDictionary<AreaGroup, int>? targetsThere) =>
        targetsThere is null
            ?
            [
                $"  {plural}:",
                .. ranked.Select(entry => $"    {TextFormat.Count(entry.Value)} {entry.Key.Name}"),
            ]
            :
            [
                $"  {plural} (removed / target objects there):",
                .. ranked.Select(entry => $"    {TextFormat.Count(entry.Value)} / {TextFormat.Count(targetsThere[entry.Key])} {entry.Key.Name}"),
            ];

    /// <summary>An area level with the plural noun its area lines list it by.</summary>
    private readonly record struct AreaLevelName(AreaLevel Level, string Plural);
}
