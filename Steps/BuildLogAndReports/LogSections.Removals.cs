using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection BuildRemovalsSection(CollectedObjects world, IEnumerable<RemovedObject> removals, ReportContext context) =>
        new(
            "removals",
            context.Detailed ? [.. removals.Select(removal => RemovalLine(world, removal, context))] : []);

    private static string RemovalLine(CollectedObjects world, RemovedObject removal, ReportContext context)
    {
        var target = world.Targets[removal.TargetIndex];
        return $"  Removed {RecordNames.Describe(target)} (base {RecordNames.DescribeBase(context.Bases, target.Base)}) "
            + $"in {Describe.Location(world, target)}, {AreaText.Detail(target.Area, world.Spaces[target.SpaceKey])}; {Describe.RemovalReason(world, removal)}";
    }

    /// <param name="withoutPlacement">Target-plugin objects not checked because they are deleted, start disabled or have no valid position.</param>
    public static LogSection BuildTargetVisibilitySection(IReadOnlyList<TargetObject> targets, int withoutPlacement, PluginName target, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("targetVisibility", []);
        var looks = targets.Select(targetObject => context.Shapes.VisibilityOf(targetObject)).ToList();
        var invisibleByKind = KeyedGroups
            .Rank(
                KeyedGroups.CountBy(looks.Where(entry => !entry.IsVisible), ObjectVisibilityText.Describe, StringComparer.Ordinal),
                StringComparer.Ordinal)
            .ToList();
        var visible = looks.Count(entry => entry.IsVisible);
        var kinds = TextLists.JoinOr(TextLists.Counts(invisibleByKind), Describe.ListSeparator, Describe.NoEntries);
        return new LogSection(
            "targetVisibility",
            [
                $"{target} objects: {TextFormat.Count(visible)} visible, {TextFormat.Count(looks.Count - visible)} invisible (by kind: {kinds}), "
                + $"{TextFormat.Count(withoutPlacement)} deleted, start disabled, or without a valid position or rotation.",
            ]);
    }

    /// <summary>
    /// What the run started from and what it did with the target's own objects: all of them, those earlier patchers in this group
    /// removed, those it did not judge, those it judged and those it removed. The moved replacements have a section of their own.
    /// </summary>
    /// <param name="removedInThisRun">The target's own objects removed in this run.</param>
    public static LogSection BuildRunSummarySection(CollectedObjects world, PluginName target, int removedInThisRun)
    {
        var counts = world.Counts;
        return new LogSection(
            "run-summary",
            [
                $"Objects of {target}: {TextFormat.Count(counts.CleanedModObjects)}; "
                + $"already removed by earlier patchers in this group: {TextFormat.Count(counts.RemovedByEarlierPatchers)}; "
                + $"not judged (winning version from a later plugin, removed by a later plugin, deleted, start disabled, or without a valid position or rotation): {TextFormat.Count(CountNotJudged(counts))}; "
                + $"judged in this run: {TextFormat.Count(CountOwnObjects(world))}; removed in this run: {TextFormat.Count(removedInThisRun)}.",
            ]);
    }

    /// <summary>The target's own objects the setup section lists as ignored.</summary>
    private static int CountNotJudged(ReadCounts counts) => counts.TargetsOverriddenLater + counts.RemovedByOtherPlugins + counts.TargetsHiddenOrWithoutPlacement;

    /// <summary>The target objects that are the target plugin's own, not moved replacements.</summary>
    private static int CountOwnObjects(CollectedObjects world) => world.Targets.Count(target => !target.IsMovedReplacement);

    /// <param name="final">The run's decisions, which tell the follow-up removals that were first shrunk or turned.</param>
    /// <param name="changedTerrain">The changed-terrain step's outcome; the "on changed terrain" item only shows when the step was performed.</param>
    /// <param name="visibleLeftBehind">The visible left-behind step's outcome; the "visible objects left behind" item only shows when the step was performed.</param>
    public static LogSection BuildRemovalSummarySection(
        CollectedObjects world,
        IReadOnlyList<RemovedObject> removals,
        IRemovalDecisions final,
        PluginName target,
        StepOutcome changedTerrain,
        StepOutcome visibleLeftBehind)
    {
        var onChangedTerrain = changedTerrain == StepOutcome.Performed
            ? $"{TextFormat.Count(removals.Count(removal => removal is TerrainChangedRemoval))} on changed terrain, "
            : string.Empty;
        var visibleLeftBehindItem = visibleLeftBehind == StepOutcome.Performed
            ? $"{TextFormat.Count(removals.Count(removal => removal is VisibleLeftBehindRemoval))} visible objects left behind, "
            : string.Empty;
        var tooClose = removals.Count(removal => removal is TooCloseRemoval);
        List<RemovedObject> followUps = [.. removals.Where(IsFollowUp)];
        var leftBehind = removals.Count(removal => removal is LeftBehindRemoval);
        var linked = removals.Count(removal => removal is LinkedRemoval);
        return new LogSection(
            "removal-summary",
            [
                $"Removed {TextFormat.Count(removals.Count)} objects ({onChangedTerrain}{TextFormat.Count(tooClose)} too close, "
                + $"{TextFormat.Count(followUps.Count)} follow-up removals{FirstShrunkOrTurned(followUps, final)}, "
                + $"{visibleLeftBehindItem}{TextFormat.Count(leftBehind)} invisible objects left behind, {TextFormat.Count(linked)} linked to removed objects)"
                + $"{OwnAndMovedRemoved(world, removals, target)}.",
            ]);
    }

    private static bool IsFollowUp(RemovedObject removal) =>
        removal is TouchingRemoval or SupportRemoval or TouchingAnotherModsObjectRemoval or SupportWithAnotherModsObjectRemoval;

    /// <summary>" (2 of them first shrunk or turned)", or nothing when none was.</summary>
    private static string FirstShrunkOrTurned(IEnumerable<RemovedObject> followUps, IRemovalDecisions final)
    {
        var firstFitted = final.Rounds.Where(round => round.Kind == RoundKind.Fit).SelectMany(round => final.FittedAtFirstIn(round)).Select(fitted => fitted.Index).ToHashSet();
        var count = followUps.Count(removal => firstFitted.Contains(removal.TargetIndex));
        return count > 0 ? $" ({TextFormat.Count(count)} of them first shrunk or turned)" : string.Empty;
    }

    /// <summary>"; 5 of them Mod.esp objects, 2 objects of other mods that Mod.esp moved", or nothing when no moved object was removed.</summary>
    private static string OwnAndMovedRemoved(CollectedObjects world, IReadOnlyList<RemovedObject> removals, PluginName target)
    {
        var moved = removals.Count(removal => world.Targets[removal.TargetIndex].IsMovedReplacement);
        return moved > 0
            ? $"; {TextFormat.Count(removals.Count - moved)} of them {target} objects, {TextFormat.Count(moved)} objects of other mods that {target} moved"
            : string.Empty;
    }

    /// <summary>Each kept target once, counted under the first reason found for it.</summary>
    public static LogSection BuildKeptSummarySection(IEnumerable<KeptObject> kept)
    {
        var keptOnce = KeyedGroups.DistinctBy(kept, entry => entry.TargetIndex, EqualityComparer<int>.Default).ToList();
        var byCategory = KeyedGroups.Rank(
            KeyedGroups.CountBy(keptOnce, entry => KeepReasonText.Category(entry.Reason), StringComparer.Ordinal),
            StringComparer.Ordinal);
        if (byCategory.Count == 0) return new LogSection("kept-summary", []);
        var because = keptOnce.Any(entry => IsKeptForAnotherModsObject(entry.Reason))
            ? "because other records depend on them, because of their own script or location ref type, or because other mods' objects rest on them."
            : "because other records depend on them or because of their own script or location ref type.";
        return new LogSection(
            "kept-summary",
            [
                $"Kept {TextFormat.Count(byCategory.Sum(group => group.Value))} objects that would have been removed, {because}",
                .. byCategory.Select(group => $"  {group.Key}: {TextFormat.Count(group.Value)}"),
            ]);
    }

    private static bool IsKeptForAnotherModsObject(KeepReason reason) =>
        reason.Kind == KeepKind.OtherModsObjectRestsOnIt || reason.Keeper?.Reason.Kind == KeepKind.OtherModsObjectRestsOnIt;

    /// <summary>
    /// The objects kept for a reason of their own: a script on them, or on their base the target plugin defines, names
    /// a quest or scene, they have a location ref type themselves, or another mod's object rests on them. They are kept on
    /// purpose and need no manual patch; objects that another record links to are in the hints instead. Each kept object
    /// once; no lines when there are none.
    /// </summary>
    public static LogSection BuildKeptOnPurposeSection(CollectedObjects world, IEnumerable<KeptObject> kept)
    {
        var keptOnce = kept.DistinctBy(entry => entry.TargetIndex).ToList();
        return new LogSection(
            "kept-on-purpose",
            [
                .. KeptOnPurposeLines(world, keptOnce, KeepKind.ScriptNamesQuestOrScene, "Kept, script names a quest or scene:", KeepReasonText.Detail),
                .. KeptOnPurposeLines(world, keptOnce, KeepKind.LocationRole, "Kept, location role:", KeepReasonText.Detail),
                .. KeptOnPurposeLines(world, keptOnce, KeepKind.OtherModsObjectRestsOnIt, "Kept, another mod's object rests on it:", KeepReasonText.RestingObjects),
            ]);
    }

    /// <param name="detail">The reason's detail, which does not repeat the header.</param>
    private static IEnumerable<string> KeptOnPurposeLines(
        CollectedObjects world, IEnumerable<KeptObject> kept, KeepKind kind, string header, Func<KeepReason, string> detail) =>
        kept.Where(entry => entry.Reason.Kind == kind).Select(entry =>
        {
            var target = world.Targets[entry.TargetIndex];
            return $"  {header} {RecordNames.Describe(target)} in {Describe.Location(world, target)}: {detail(entry.Reason)}.";
        });

    public static LogSection BuildMarkersByTypeSection(IReadOnlyList<TargetObject> targets, IEnumerable<RemovedObject> removals, ReportContext context)
    {
        var counts = KeyedGroups
            .CountBy(removals.Select(removal => context.Shapes.VisibilityOf(targets[removal.TargetIndex]).Kind).OfType<InvisibleObjectKind>(), kind => kind, EqualityComparer<InvisibleObjectKind>.Default)
            .OrderBy(group => group.Key)
            .ToList();
        if (counts.Count == 0) return new LogSection("markers-by-type", []);
        var named = counts.Select(group => KeyValuePair.Create(ObjectVisibilityText.KindName(group.Key), group.Value));
        return new LogSection("markers-by-type", [$"Removed markers by type: {string.Join(Describe.ListSeparator, TextLists.Counts(named))}."]);
    }
}
