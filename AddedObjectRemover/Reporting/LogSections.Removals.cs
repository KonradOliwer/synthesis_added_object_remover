using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    public static LogSection Removals(World world, IEnumerable<Removal> removals, ReportContext context) =>
        new(
            "removals",
            context.Detailed ? [.. removals.Select(removal => RemovalLine(world, removal, context))] : []);

    private static string RemovalLine(World world, Removal removal, ReportContext context)
    {
        var target = world.Targets[removal.TargetIndex];
        return $"  Removed {RecordNames.Describe(target)} (base {RecordNames.DescribeBase(context.Bases, target.Base)}) "
            + $"in {Describe.Location(world, target)}; {Describe.RemovalReason(world, removal)}";
    }

    public static LogSection RemovalSummary(IReadOnlyList<Removal> removals)
    {
        var tooClose = removals.Count(removal => removal is TooCloseRemoval);
        var followUp = removals.Count(removal => removal is TouchingRemoval or AnchoringRemoval);
        var leftovers = removals.Count(removal => removal is LeftoverRemoval);
        var linked = removals.Count(removal => removal is LinkedRemoval);
        return new LogSection(
            "removal-summary",
            [
                $"Removed {removals.Count:N0} objects ({tooClose:N0} too close, {followUp:N0} follow-up removals, "
                + $"{leftovers:N0} leftover invisible objects, {linked:N0} linked to removed objects).",
            ]);
    }

    /// <summary>Each kept target once, counted under the first reason found for it.</summary>
    public static LogSection KeptSummary(IEnumerable<KeptTarget> kept)
    {
        var byCategory = kept
            .DistinctBy(entry => entry.TargetIndex)
            .GroupBy(entry => entry.Reason.Category)
            .Select(group => (Category: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Category, StringComparer.Ordinal)
            .ToList();
        return new LogSection(
            "kept-summary",
            [
                $"Kept {byCategory.Sum(group => group.Count):N0} objects that would have been removed because other records depend on them.",
                .. byCategory.Select(group => $"  {group.Category}: {group.Count:N0}"),
            ]);
    }

    public static LogSection MarkersByType(IEnumerable<Removal> removals, TargetLooks looks)
    {
        var counts = removals
            .Select(removal => looks.ByTarget[removal.TargetIndex].Kind)
            .OfType<InvisibleObjectKind>()
            .GroupBy(kind => kind)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Count():N0} {group.Key}")
            .ToList();
        return new LogSection("markers-by-type", [$"Removed markers by type: {(counts.Count == 0 ? "none" : string.Join(", ", counts))}."]);
    }
}
