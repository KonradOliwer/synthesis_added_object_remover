using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    private const string AreaIndentStep = "  ";
    private const string JoinedAreaSeparator = " › ";

    /// <summary>
    /// Where the objects of each closing summary are, as one tree by worldspace, hold and place: the removals, the kept
    /// objects, the removed markers, the kept objects to check by hand and each removal warning; only summaries with objects have lines.
    /// </summary>
    public static LogSection BuildAreasSection(
        CollectedObjects world,
        IReadOnlyList<RemovedObject> removals,
        IEnumerable<KeptObject> kept,
        IEnumerable<ManualPatchHint> hints,
        RemovalRisks risks)
    {
        List<int> removedTargets = [.. removals.Select(removal => removal.TargetIndex)];
        var removed = removedTargets.ToHashSet();
        var keptOnce = KeyedGroups.DistinctBy(kept, entry => entry.TargetIndex, EqualityComparer<int>.Default);
        var hintList = hints.ToList();
        var removedMarkers = HintTargets(hintList.Where(IsRemovedMarker));
        var keptToCheck = HintTargets(hintList.Where(hint => !IsRemovedMarker(hint)));
        var uniqueActors = RemovedUniqueActors(risks, removed).Select(role => role.Target.Index).Distinct().ToList();
        var largeReferences = RemovedLargeReferences(risks, removed).ToList();
        var tree = AreaTree.Of(world);
        return new LogSection(
            "areas",
            [
                .. AreaLines(tree, removedTargets, $"Removed {OfAllCheckedObjects(tree, removedTargets.Count, "removed")}:"),
                .. KeptAreaLines(tree, keptOnce),
                .. AreaLines(tree, removedMarkers, $"{RemovedMarkersHeading}: {TextFormat.Count(removedMarkers.Count)} of {TextFormat.Count(tree.Total)}:"),
                .. AreaLines(tree, keptToCheck, $"{KeptObjectsToCheckHeading}: {TextFormat.Count(keptToCheck.Count)} of {TextFormat.Count(tree.Total)}:"),
                .. AreaLines(tree, uniqueActors, $"Removed {OfAllCheckedObjects(tree, uniqueActors.Count, "unique NPCs")}:"),
                .. AreaLines(tree, largeReferences, $"Removed {OfAllCheckedObjects(tree, largeReferences.Count, "large-reference list objects")}:"),
            ]);
    }

    private static List<int> HintTargets(IEnumerable<ManualPatchHint> hints) => [.. hints.Select(hint => hint.TargetIndex).Distinct()];

    /// <summary>E.g. "3022/4076 (removed/all checked objects)": the checked objects are the cleaned mod's and the other mods' objects it moved.</summary>
    private static string OfAllCheckedObjects(AreaTree tree, int count, string what) =>
        $"{TextFormat.Count(count)}/{TextFormat.Count(tree.Total)} ({what}/all checked objects)";

    /// <summary>One tree per kept kind, the kind with the most objects first, as in the kept summary.</summary>
    private static IEnumerable<string> KeptAreaLines(AreaTree tree, IReadOnlyList<KeptObject> keptOnce) =>
        KeyedGroups.IndicesByKey(keptOnce, entry => KeepReasonText.Category(entry.Reason), StringComparer.Ordinal)
            .OrderByDescending(kind => kind.Indices.Count)
            .ThenBy(kind => kind.Key, StringComparer.Ordinal)
            .SelectMany(kind => AreaLines(
                tree,
                [.. kind.Indices.Select(index => keptOnce[index].TargetIndex)],
                $"Kept {OfAllCheckedObjects(tree, kind.Indices.Count, "kept")}, {kind.Key}:"));

    private static IEnumerable<string> AreaLines(AreaTree tree, IReadOnlyList<int> targets, string header) =>
        targets.Count == 0 ? [] : [header, .. tree.NodesOf(targets).SelectMany(worldspace => AreaNodeLines(worldspace, depth: 0))];

    /// <summary>
    /// E.g. "    Whiterun hold 800/3000": the area, indented by its level, then its listed objects / target objects there. An area
    /// with a single child gets no line of its own; the chain is one line at the level of its first name, e.g.
    /// "    Riften hold › Riften 2/2", with the counts of its last area and a name that repeats an earlier one in it left out.
    /// </summary>
    private static IEnumerable<string> AreaNodeLines(AreaNode node, int depth)
    {
        List<AreaNode> chain = [node];
        while (chain[^1].Children.Count == 1) chain.Add(chain[^1].Children[0]);
        var last = chain[^1];
        var names = string.Join(JoinedAreaSeparator, chain.Select(area => area.Name).Distinct(StringComparer.Ordinal));
        return
        [
            $"{string.Concat(Enumerable.Repeat(AreaIndentStep, depth + 1))}{names} {TextFormat.Count(last.Count)}/{TextFormat.Count(last.TargetsThere)}",
            .. last.Children.SelectMany(child => AreaNodeLines(child, depth + chain.Count)),
        ];
    }
}
