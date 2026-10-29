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
    public static LogSection BuildRemovalsSection(CollectedObjects world, IEnumerable<ObjectWithReason> removals, IRemovalDecisions final, ReportContext context) =>
        new(
            "removals",
            context.Detailed ? [.. removals.Select(removal => $"  Removed {ObjectLine(world, removal, final, context)}")] : []);

    /// <summary>The object, where it stands and why it was taken from there, as the detailed removal and restore lines word it.</summary>
    private static string ObjectLine(CollectedObjects world, ObjectWithReason entry, IRemovalDecisions final, ReportContext context)
    {
        var target = world.Targets[entry.TargetIndex];
        return $"{RecordNames.Describe(target)} (base {RecordNames.DescribeBase(context.Bases, target.Base)}) "
            + $"in {Describe.Location(world, target)}, {AreaText.Detail(target.Area, world.Spaces[target.SpaceKey])}; {Describe.RemovalReason(world, entry, final)}";
    }

    /// <remarks>Counts the target's own objects only, like <see cref="BuildRunSummarySection"/>.</remarks>
    /// <param name="withoutPlacement">Target-plugin objects not checked because they are deleted, start disabled or have no valid position.</param>
    public static LogSection BuildTargetVisibilitySection(IReadOnlyList<TargetObject> targets, int withoutPlacement, PluginName target, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("targetVisibility", []);
        var looks = TargetOrigins.Of(targets, TargetOrigin.CleanedMod).Select(targetObject => context.Shapes.VisibilityOf(targetObject)).ToList();
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
    /// removed, those it did not judge, those it judged and those it removed. The changed objects have a section of their own.
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
                + $"judged in this run: {TextFormat.Count(TargetOrigins.CountOwn(world.Targets))}; removed in this run: {TextFormat.Count(removedInThisRun)}.",
            ]);
    }

    /// <summary>The target's own objects the setup section lists as ignored.</summary>
    private static int CountNotJudged(ReadCounts counts) => counts.TargetsOverriddenLater + counts.RemovedByOtherPlugins + counts.TargetsHiddenOrWithoutPlacement;

    /// <param name="final">The run's decisions, which tell the follow-up removals that were first shrunk or turned.</param>
    /// <param name="changedTerrain">The changed-terrain step's outcome; the "on changed terrain" item only shows when the step was performed.</param>
    /// <param name="visibleLeftBehind">The visible left-behind step's outcome; the "visible objects left behind" item only shows when the step was performed.</param>
    public static LogSection BuildRemovalSummarySection(
        CollectedObjects world,
        IReadOnlyList<ObjectWithReason> removals,
        IRemovalDecisions final,
        PluginName target,
        StepOutcome changedTerrain,
        StepOutcome visibleLeftBehind) =>
        new(
            "removal-summary",
            [
                $"Removed {TextFormat.Count(removals.Count)} objects ({ReasonItems.Describe(removals, final, changedTerrain, visibleLeftBehind, ReasonItems.ForRemovals)})"
                + $"{OwnAndChangedRemoved(world, removals, target)}.",
            ]);

    /// <summary>"; 5 of them Mod.esp objects, 2 objects of other mods that Mod.esp changed", or nothing when only own objects were removed.</summary>
    private static string OwnAndChangedRemoved(CollectedObjects world, IReadOnlyList<ObjectWithReason> removals, PluginName target)
    {
        var removed = TargetOrigins.At(world, removals.Select(removal => removal.TargetIndex));
        var ownCount = TargetOrigins.CountOwn(removed);
        return ownCount < removed.Count
            ? $"; {TextFormat.Count(ownCount)} of them {TargetOrigins.Phrase(TargetOrigin.CleanedMod, target)}, "
                + $"{TextFormat.Count(removed.Count - ownCount)} {TargetOrigins.ChangedPhrase(target)}"
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

    public static LogSection BuildMarkersByTypeSection(IReadOnlyList<TargetObject> targets, IEnumerable<ObjectWithReason> removals, ReportContext context)
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
