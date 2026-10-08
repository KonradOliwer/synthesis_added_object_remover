using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection Removals(CollectedObjects world, IEnumerable<RemovedObject> removals, ReportContext context) =>
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
    public static LogSection TargetVisibility(IReadOnlyList<TargetObject> targets, int withoutPlacement, ReportContext context)
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

    public static LogSection RemovalSummary(IReadOnlyList<RemovedObject> removals)
    {
        var tooClose = removals.Count(removal => removal is TooCloseRemoval);
        var alsoRemoved = removals.Count(removal => removal is TouchingRemoval or AnchoringRemoval);
        var leftBehind = removals.Count(removal => removal is LeftBehindRemoval);
        var linked = removals.Count(removal => removal is LinkedRemoval);
        return new LogSection(
            "removal-summary",
            [
                $"Removed {TextFormat.Count(removals.Count)} objects ({TextFormat.Count(tooClose)} too close, {TextFormat.Count(alsoRemoved)} follow-up removals, "
                + $"{TextFormat.Count(leftBehind)} leftover invisible objects, {TextFormat.Count(linked)} linked to removed objects).",
            ]);
    }

    /// <summary>Each kept target once, counted under the first reason found for it.</summary>
    public static LogSection KeptSummary(IEnumerable<KeptObject> kept)
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
                $"Kept {TextFormat.Count(byCategory.Sum(group => group.Value))} objects that would have been removed because other records depend on them.",
                .. byCategory.Select(group => $"  {group.Key}: {TextFormat.Count(group.Value)}"),
            ]);
    }

    public static LogSection MarkersByType(IReadOnlyList<TargetObject> targets, IEnumerable<RemovedObject> removals, ReportContext context)
    {
        var counts = KeyedGroups
            .CountBy(removals.Select(removal => context.Shapes.VisibilityOf(targets[removal.TargetIndex]).Kind).OfType<InvisibleObjectKind>(), kind => kind, EqualityComparer<InvisibleObjectKind>.Default)
            .OrderBy(group => group.Key);
        return new LogSection("markers-by-type", [$"Removed markers by type: {TextLists.JoinOr(TextLists.Counts(counts), Describe.ListSeparator, Describe.NoEntries)}."]);
    }
}
