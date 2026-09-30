using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    /// <summary>What the load-order read found; the list of overridden other-mod objects and the index sizes only the detailed log shows.</summary>
    public static LogSection Scan(World world, ModKey target, TimeSpan elapsed, ReportContext context)
    {
        var counts = world.Counts;
        var lines = ImmutableArray.CreateBuilder<string>();
        if (context.Detailed) lines.AddRange(OverriddenOthers(world, target));
        lines.Add(
            $"Scanned {counts.RecordsScanned:N0} placed records{Describe.TimedSuffix(elapsed, context)}: "
            + $"{world.Targets.Length:N0} {target} objects to check, "
            + $"{world.Rivals.Length:N0} objects from other mods.");
        if (counts.TargetsOverriddenLater > 0)
        {
            lines.Add($"  Ignored {counts.TargetsOverriddenLater:N0} {target} objects whose winning version comes from a later plugin.");
        }
        if (counts.TargetsHiddenOrWithoutPlacement > 0)
        {
            lines.Add($"  Ignored {counts.TargetsHiddenOrWithoutPlacement:N0} {target} objects that are initially disabled or have no valid position or rotation.");
        }
        if (context.Detailed && counts.OtherInvalidPlacements > 0)
        {
            lines.Add($"  Ignored {counts.OtherInvalidPlacements:N0} other-mod objects in the spaces of {target} objects whose position or rotation is out of range or not a number.");
        }
        if (counts.OthersOverriddenByTarget > 0)
        {
            lines.Add($"  Ignored {counts.OthersOverriddenByTarget:N0} other-mod objects that {target} itself overrides.");
        }
        if (context.Detailed) lines.AddRange(RecordedCounts(world));
        return new LogSection("scan", lines.ToImmutable());
    }

    public static LogSection Threads(Execution execution, ReportContext context) =>
        new("threads", context.Detailed ? [$"Using {execution.Workers} threads."] : []);

    /// <summary>The rivals of the target spaces, which the other steps look at.</summary>
    public static LogSection Index(World world, TimeSpan elapsed, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("index", []);
        var targetSpaces = world.TargetSpaces();
        var rivals = world.Rivals.Count(rival => targetSpaces.Contains(rival.SpaceKey));
        return new LogSection(
            "index",
            [$"Indexed {rivals:N0} other objects in {targetSpaces.Count:N0} cells/worldspaces in {Describe.Seconds(elapsed)}."]);
    }

    public static LogSection WarmUp(int targetBaseCount, TimeSpan elapsed, PhaseProblems problems, ReportContext context)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.AddRange(Problems(problems, context));
        if (context.Detailed) lines.Add($"Bounds warm-up: {targetBaseCount:N0} target base objects in {Describe.Seconds(elapsed)}.");
        return new LogSection("warmUp", lines.ToImmutable());
    }

    /// <param name="withoutPlacement">Target-plugin objects not checked because they are initially disabled or have no valid position.</param>
    public static LogSection TargetVisibility(TargetLooks looks, int withoutPlacement, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("targetVisibility", []);
        var invisibleByKind = looks.ByTarget
            .Where(entry => !entry.IsVisible)
            .GroupBy(entry => entry.Describe())
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Count():N0} {group.Key}")
            .ToList();
        var visible = looks.ByTarget.Count(entry => entry.IsVisible);
        var kinds = invisibleByKind.Count == 0 ? "none" : string.Join(", ", invisibleByKind);
        return new LogSection(
            "targetVisibility",
            [
                $"Target objects: {visible:N0} visible, {looks.ByTarget.Length - visible:N0} invisible (by kind: {kinds}), "
                + $"{withoutPlacement:N0} without placement/disabled.",
            ]);
    }

    /// <param name="linkCount">Links from target objects to any target-plugin record, before those not between two target objects are dropped.</param>
    public static LogSection Groups(LinkedGroups groups, int linkCount, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("groups", []);
        var multiMember = groups.MultiMemberGroups.ToList();
        return new LogSection(
            "groups",
            [
                $"Linked groups: {multiMember.Count:N0} groups of linked target objects ({multiMember.Sum(members => members.Count):N0} objects) "
                + $"from {linkCount:N0} links to target-plugin records.",
            ]);
    }

    /// <summary>The replaced rivals, then the problems met while matching, then the summary.</summary>
    public static LogSection Replacements(World world, Replacements replacements, PhaseProblems problems, TimeSpan elapsed, ReportContext context) =>
        new("replacements",
        [
            .. context.Detailed ? ReplacementList(world, replacements) : [],
            .. Problems(problems, context),
            $"Replacement matching: {replacements.Count:N0} other-mod objects excluded{Describe.TimedSuffix(elapsed, context)}.",
        ]);

    private static IEnumerable<string> OverriddenOthers(World world, ModKey target) =>
        world.OverriddenOthers.Select(other =>
            $"  Ignored other-mod object overridden by {target}: "
            + $"{RecordNames.Describe(other.FormKey, other.EditorId)} {RecordNames.DescribeOrigin(other.FormKey, other.WinningMod)}.");

    private static IEnumerable<string> RecordedCounts(World world)
    {
        var counts = world.Counts;
        var supporterCount = world.Backdrop.IsCollected ? world.Rivals.Length + world.Backdrop.Value.Length : 0;
        if (supporterCount > 0)
        {
            yield return $"  Recorded {supporterCount:N0} placed objects of any plugin as possible supporters or obstacles.";
        }
        if (counts.NavmeshCount > 0) yield return $"  Recorded {counts.NavmeshCount:N0} navmeshes.";
    }

    private static IEnumerable<string> ReplacementList(World world, Replacements replacements)
    {
        var entries = replacements.List
            .Select(replacement => (Replacement: replacement, Rival: world.Rivals[replacement.Rival.Index]))
            .OrderBy(entry => entry.Rival.FormKey.ToString(), StringComparer.Ordinal);
        foreach (var (replacement, rival) in entries)
        {
            var target = world.Targets[replacement.By.Index];
            yield return $"  Ignored replaced object {RecordNames.Describe(rival.FormKey, rival.EditorId)} from {rival.WinningMod}: "
                + $"replaced by {RecordNames.Describe(target.Key, target.EditorId)}, "
                + $"distance {replacement.Distance:F1}, size ratio {replacement.SizeRatio:F2}.";
        }
    }
}
