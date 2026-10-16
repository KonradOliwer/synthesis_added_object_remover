using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

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
            + $"in {Describe.Location(world, target)}; {Describe.RemovalReason(world, removal)}";
    }

    /// <param name="withoutPlacement">Target-plugin objects not checked because they are initially disabled or have no valid position.</param>
    public static LogSection BuildTargetVisibilitySection(IReadOnlyList<TargetObject> targets, int withoutPlacement, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("targetVisibility", []);
        var looks = targets.Select(target => context.Shapes.VisibilityOf(target)).ToList();
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
                $"Target objects: {TextFormat.Count(visible)} visible, {TextFormat.Count(looks.Count - visible)} invisible (by kind: {kinds}), "
                + $"{TextFormat.Count(withoutPlacement)} without placement/disabled.",
            ]);
    }

    /// <param name="changedTerrain">The changed-terrain step's outcome; the "on changed terrain" item only shows when the step was performed.</param>
    public static LogSection BuildRemovalSummarySection(IReadOnlyList<RemovedObject> removals, StepOutcome changedTerrain)
    {
        var onChangedTerrain = changedTerrain == StepOutcome.Performed
            ? $"{TextFormat.Count(removals.Count(removal => removal is TerrainChangedRemoval))} on changed terrain, "
            : string.Empty;
        var tooClose = removals.Count(removal => removal is TooCloseRemoval);
        var alsoRemoved = removals.Count(removal => removal is TouchingRemoval or SupportRemoval);
        var leftBehind = removals.Count(removal => removal is LeftBehindRemoval);
        var linked = removals.Count(removal => removal is LinkedRemoval);
        return new LogSection(
            "removal-summary",
            [
                $"Removed {TextFormat.Count(removals.Count)} objects ({onChangedTerrain}{TextFormat.Count(tooClose)} too close, {TextFormat.Count(alsoRemoved)} follow-up removals, "
                + $"{TextFormat.Count(leftBehind)} leftover invisible objects, {TextFormat.Count(linked)} linked to removed objects).",
            ]);
    }

    /// <summary>Each kept target once, counted under the first reason found for it.</summary>
    public static LogSection BuildKeptSummarySection(IEnumerable<KeptObject> kept)
    {
        var byCategory = KeyedGroups.Rank(
            KeyedGroups.CountBy(
                KeyedGroups.DistinctBy(kept, entry => entry.TargetIndex, EqualityComparer<int>.Default),
                entry => KeepReasonText.Category(entry.Reason),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        return new LogSection(
            "kept-summary",
            [
                $"Kept {TextFormat.Count(byCategory.Sum(group => group.Value))} objects that would have been removed, "
                + "because other records depend on them or because of their own script or location ref type.",
                .. byCategory.Select(group => $"  {group.Key}: {TextFormat.Count(group.Value)}"),
            ]);
    }

    /// <summary>
    /// The objects kept for a property of their own: a script on them, or on their base the target plugin defines, names
    /// a quest or scene, or they have a location ref type themselves. They are kept on purpose and need no manual patch;
    /// objects that another record links to are in the hints instead. Each kept object once; no lines when there are none.
    /// </summary>
    public static LogSection BuildKeptOnPurposeSection(CollectedObjects world, IEnumerable<KeptObject> kept)
    {
        var keptOnce = kept.DistinctBy(entry => entry.TargetIndex).ToList();
        return new LogSection(
            "kept-on-purpose",
            [
                .. KeptOnPurposeLines(world, keptOnce, KeepKind.ScriptNamesQuestOrScene, "Kept, script names a quest or scene:"),
                .. KeptOnPurposeLines(world, keptOnce, KeepKind.LocationRole, "Kept, location role:"),
            ]);
    }

    private static IEnumerable<string> KeptOnPurposeLines(CollectedObjects world, IEnumerable<KeptObject> kept, KeepKind kind, string header) =>
        kept.Where(entry => entry.Reason.Kind == kind).Select(entry =>
        {
            var target = world.Targets[entry.TargetIndex];
            return $"  {header} {RecordNames.Describe(target)} in {Describe.Location(world, target)}: {KeepReasonText.Detail(entry.Reason)}.";
        });

    public static LogSection BuildMarkersByTypeSection(IReadOnlyList<TargetObject> targets, IEnumerable<RemovedObject> removals, ReportContext context)
    {
        var counts = KeyedGroups
            .CountBy(removals.Select(removal => context.Shapes.VisibilityOf(targets[removal.TargetIndex]).Kind).OfType<InvisibleObjectKind>(), kind => kind, EqualityComparer<InvisibleObjectKind>.Default)
            .OrderBy(group => group.Key);
        return new LogSection("markers-by-type", [$"Removed markers by type: {TextLists.JoinOr(TextLists.Counts(counts), Describe.ListSeparator, Describe.NoEntries)}."]);
    }
}
