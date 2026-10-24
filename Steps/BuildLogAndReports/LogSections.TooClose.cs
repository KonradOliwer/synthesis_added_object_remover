using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The too-close section when the step is switched off: it prints nothing.</summary>
    public static LogSection BuildSwitchedOffTooCloseSection() => new("tooClose", []);

    public static LogSection BuildTooCloseSection(TooCloseSection report, ReportContext context) =>
        new("tooClose",
        [
            .. context.Detailed && report.Result.Zone == ZoneShape.ObjectShape ? ShapeZoneLines(report) : [],
            $"Found {TextFormat.Count(report.Result.Hits.Length)} of {TextFormat.Count(report.World.Targets.Length)} {report.Target} objects {DescribeDistance(report.Result)}"
                + $"{Describe.TimedSuffix(report.Elapsed, context)}{DescribeAlreadyRemoved(report.AlreadyRemoved)}.",
            .. context.Detailed ? InvisibleOthers(report.Census) : [],
            .. context.Detailed ? NpcLines(report) : [],
            .. BuildKeptLines(report.World, report.Kept),
        ]);

    /// <summary>How close the found objects are to other mods' objects; with both removal zones on, how many each zone found.</summary>
    private static string DescribeDistance(TooCloseResult result)
    {
        if (result.ZonesTested is [TooCloseCause.Distance { Units: var units }]) return $"within {units} units of other mods' objects";
        if (result.ZonesTested.Length == 1) return "too close to other mods' objects";

        var byDistance = result.Hits.Count(hit => hit.Cause is TooCloseCause.Distance);
        var bySize = result.Hits.Length - byDistance;
        return $"too close to other mods' objects ({TextFormat.Count(bySize)} by object size, {TextFormat.Count(byDistance)} by distance)";
    }

    private static string DescribeAlreadyRemoved(int count) =>
        count > 0 ? $"; {TextFormat.Count(count)} of them were already removed on changed terrain" : string.Empty;

    private static ImmutableArray<string> ShapeZoneLines(TooCloseSection report)
    {
        var stats = report.Result.Work.Zone;
        return
        [
            $"Object-shape zones: {TextFormat.Count(stats.CandidatePairs)} candidate pairs, {TextFormat.Count(stats.BoxFilterPasses)} passed the box filter, "
                + $"{TextFormat.Count(stats.NarrowTests)} mesh tests, {TextFormat.Count(stats.Hits)} hits, {TextFormat.Count(stats.CentrePointFallbacks)} centre-point fallbacks "
                + $"(other object without mesh triangles), {TextFormat.Count(stats.BoxZoneTargets)} {report.Target} objects without mesh triangles used their box; "
                + $"{TextFormat.Count(stats.TrianglePairsTested)} triangle pairs tested exactly.",
            $"  {TextFormat.Count(report.Result.LargeOtherObjects)} large other objects tested against every {report.Target} object of their space; "
                + $"{TextFormat.Count(report.EffectOnlyMeshes)} effect-only meshes (fog, light rays, water spray) ignored.",
            $"  Meshes indexed: {TextFormat.Count(report.MeshesIndexed)} ({TextFormat.Count(report.MeshTriangles)} triangles).",
        ];
    }

    private static IEnumerable<string> InvisibleOthers(InvisibleOtherObjectCounts census)
    {
        var total = census.InvisibleByReason.Sum(entry => entry.Value);
        if (total == 0) yield break;
        yield return $"  Ignored {TextFormat.Count(total)} other-mod objects that are invisible (markers, lights, sounds, decals, trigger boxes, ...).";
        foreach (var (reason, count) in census.InvisibleByReason) yield return $"    {reason}: {TextFormat.Count(count)}";
    }

    private static IEnumerable<string> NpcLines(TooCloseSection report) => report.Result.Npcs switch
    {
        NpcHandling.OnlyWhenStuckInsideTheObject => StuckNpcLines(GetStuckNpcSummary(report), report, withCollision: false),
        NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision => StuckNpcLines(GetStuckNpcSummary(report), report, withCollision: true),
        NpcHandling.Never => [$"NPCs and creatures: ignored; {TextFormat.Count(report.Census.PlacedNpcs)} placed NPCs of other mods never cause removals."],
        NpcHandling.AlwaysLikeAnyObject => [],
        _ => throw new UnreachableException($"Unknown NPC handling {report.Result.Npcs}."),
    };

    private static NpcStuckSummary GetStuckNpcSummary(TooCloseSection report) =>
        report.Result.Npc ?? throw new InvalidOperationException("The too-close step handled NPCs only when stuck but gave no stuck-NPC summary.");

    /// <param name="withCollision">Only objects whose mesh collision stops characters count.</param>
    private static IEnumerable<string> StuckNpcLines(NpcStuckSummary summary, TooCloseSection report, bool withCollision)
    {
        var sizes = summary.Sizes;
        var mode = withCollision ? "only when stuck in the object and the object's collision stops characters" : "only when stuck in the object";
        var notCounted = withCollision
            ? $", {TextFormat.Count(summary.ConflictsWithoutBlockingCollision)} of them not counted because their collision does not stop characters"
            : string.Empty;
        yield return $"NPCs and creatures ({mode}): {TextFormat.Count(sizes.Evaluated)} placed NPCs evaluated: "
            + $"{TextFormat.Count(sizes.ByBodyMesh)} sized by body mesh, {TextFormat.Count(sizes.ByObjectBounds)} by Object Bounds, "
            + $"{TextFormat.Count(sizes.ByHumanoidApproximation)} by humanoid approximation, {TextFormat.Count(sizes.ByPoint)} sized as a point; "
            + $"{TextFormat.Count(sizes.WithoutNpc)} skipped because their base is no NPC; "
            + $"{TextFormat.Count(summary.PairsTested)} NPC-object pairs tested ({TextFormat.Count(summary.CoreTests)} body boxes), "
            + $"{TextFormat.Count(summary.Conflicts)} objects with an NPC stuck in them{notCounted}.";
        yield return $"  NPC body cache: {TextFormat.Count(report.BodiesBuilt)} bodies built; "
            + $"{TextFormat.Count(report.NpcBasesResolved)} NPC bases resolved; "
            + $"{TextFormat.Count(report.LeveledListsResolved)} leveled lists resolved; "
            + $"{TextFormat.Count(report.BodyMeshSetsMeasured)} body mesh sets measured.";
        foreach (var fallback in summary.PointFallbacks)
        {
            yield return $"  Sized as a point {Describe.OtherObject(fallback.Npc)}: {Describe.PointReason(fallback.Reason)}.";
        }
    }
}
