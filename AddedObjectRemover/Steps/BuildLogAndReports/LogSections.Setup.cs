using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>What the load-order read found; the list of overridden other-mod objects and the index sizes only the detailed log shows.</summary>
    public static LogSection Scan(CollectedObjects world, PluginName target, TimeSpan elapsed, ReportContext context)
    {
        var counts = world.Counts;
        var lines = ImmutableArray.CreateBuilder<string>();
        if (context.Detailed) lines.AddRange(OverriddenOthers(world, target));
        lines.Add(
            $"Scanned {TextFormat.Count(counts.RecordsScanned)} placed records{Describe.TimedSuffix(elapsed, context)}: "
            + $"{TextFormat.Count(world.Targets.Length)} {target} objects to check, "
            + $"{TextFormat.Count(world.OtherModObjects.Length)} objects from other mods.");
        if (counts.TargetsOverriddenLater > 0)
        {
            lines.Add($"  Ignored {TextFormat.Count(counts.TargetsOverriddenLater)} {target} objects whose winning version comes from a later plugin.");
        }
        if (counts.TargetsHiddenOrWithoutPlacement > 0)
        {
            lines.Add($"  Ignored {TextFormat.Count(counts.TargetsHiddenOrWithoutPlacement)} {target} objects that are initially disabled or have no valid position or rotation.");
        }
        if (context.Detailed && counts.OtherInvalidPlacements > 0)
        {
            lines.Add($"  Ignored {TextFormat.Count(counts.OtherInvalidPlacements)} other-mod objects in the spaces of {target} objects whose position or rotation is out of range or not a number.");
        }
        if (counts.OthersOverriddenByTarget > 0)
        {
            lines.Add($"  Ignored {TextFormat.Count(counts.OthersOverriddenByTarget)} other-mod objects that {target} itself overrides.");
        }
        if (context.Detailed) lines.AddRange(RecordedCounts(world));
        return new LogSection("scan", lines.ToImmutable());
    }

    public static LogSection Threads(Execution execution, ReportContext context) =>
        new("threads", context.Detailed ? [$"Using {execution.Workers} threads."] : []);

    /// <summary>The other-mod objects of the target spaces, which the other steps look at.</summary>
    public static LogSection Index(CollectedObjects world, TimeSpan elapsed, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("index", []);
        var targetSpaces = world.TargetSpaces();
        var otherModObjects = world.OtherModObjects.Count(other => targetSpaces.Contains(other.SpaceKey));
        return new LogSection(
            "index",
            [$"Indexed {TextFormat.Count(otherModObjects)} other objects in {TextFormat.Count(targetSpaces.Count)} cells/worldspaces in {TextFormat.Seconds(elapsed)}."]);
    }

    /// <param name="linkCount">Links from target objects to any target-plugin record, before those not between two target objects are dropped.</param>
    public static LogSection Groups(ILinkedGroups groups, int linkCount, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("groups", []);
        var multiMember = groups.MultiMemberGroups.ToList();
        return new LogSection(
            "groups",
            [
                $"Linked groups: {TextFormat.Count(multiMember.Count)} groups of linked target objects ({TextFormat.Count(multiMember.Sum(members => members.Count))} objects) "
                + $"from {TextFormat.Count(linkCount)} links to target-plugin records.",
            ]);
    }

    /// <summary>The replaced other-mod objects, then the problems met while matching, then the summary.</summary>
    public static LogSection Replacements(CollectedObjects world, Replacements replacements, PhaseProblems problems, TimeSpan elapsed, ReportContext context) =>
        new("replacements",
        [
            .. context.Detailed ? ReplacementList(world, replacements) : [],
            .. Problems(problems, context),
            $"Replacement matching: {TextFormat.Count(replacements.Count)} other-mod objects excluded{Describe.TimedSuffix(elapsed, context)}.",
        ]);

    private static IEnumerable<string> OverriddenOthers(CollectedObjects world, PluginName target) =>
        world.OverriddenOthers.Select(other =>
            $"  Ignored other-mod object overridden by {target}: "
            + $"{RecordLabels.Of(other.Key, other.EditorId)} {Describe.OriginLabel(other.Key, other.WinningMod)}.");

    private static IEnumerable<string> RecordedCounts(CollectedObjects world)
    {
        var counts = world.Counts;
        var supporterCount = world.SupportOnlyObjects is { } supportOnly ? world.OtherModObjects.Length + supportOnly.Length : 0;
        if (supporterCount > 0)
        {
            yield return $"  Recorded {TextFormat.Count(supporterCount)} placed objects of any plugin as possible supporters or obstacles.";
        }
        if (counts.NavmeshCount > 0) yield return $"  Recorded {TextFormat.Count(counts.NavmeshCount)} navmeshes.";
    }

    private static IEnumerable<string> ReplacementList(CollectedObjects world, Replacements replacements)
    {
        var entries = replacements.List
            .Select(replacement => (Replacement: replacement, Other: world.OtherModObjects[replacement.OtherModObject.Index]))
            .OrderBy(entry => entry.Other.Key, RecordKeyTextOrder.Comparer);
        foreach (var (replacement, other) in entries)
        {
            var target = world.Targets[replacement.By.Index];
            yield return $"  Ignored replaced object {RecordLabels.Of(other.Key, other.EditorId)} from {other.WinningMod}: "
                + $"replaced by {RecordLabels.Of(target.Key, target.EditorId)}, "
                + $"distance {TextFormat.Fixed(replacement.Distance, 1)}, size ratio {TextFormat.Fixed(replacement.SizeRatio, 2)}.";
        }
    }
}
