using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <remarks>Mesh problems come right after the shape-zone statistics, the phase's own meshes, and before the summary.</remarks>
    public static LogSection TooClose(TooCloseSection report, ReportContext context) =>
        new("tooClose",
        [
            .. context.Detailed && report.Options.Zone == ZoneShape.ObjectShape ? ShapeZoneLines(report) : [],
            .. Problems(report.Problems, context),
            $"Found {TextFormat.Count(report.Result.Hits.Length)} of {TextFormat.Count(report.World.Targets.Length)} {report.Target} objects too close to other mods' objects"
                + $"{Describe.TimedSuffix(report.Elapsed, context)}.",
            .. context.Detailed ? InvisibleOthers(report.Census) : [],
            .. context.Detailed ? NpcLines(report) : [],
            .. Kept(report.World, report.Kept, context),
        ]);

    private static ImmutableArray<string> ShapeZoneLines(TooCloseSection report)
    {
        var stats = report.Result.Work.Zone;
        return
        [
            $"Object-shape zones: {TextFormat.Count(stats.CandidatePairs)} candidate pairs, {TextFormat.Count(stats.BoxFilterPasses)} passed the box filter, "
                + $"{TextFormat.Count(stats.NarrowTests)} mesh tests, {TextFormat.Count(stats.Hits)} hits, {TextFormat.Count(stats.CentrePointFallbacks)} centre-point fallbacks "
                + $"(other object without mesh triangles), {TextFormat.Count(stats.BoxZoneTargets)} targets without mesh triangles used their box; "
                + $"{TextFormat.Count(stats.TrianglePairsTested)} triangle pairs tested exactly.",
            $"  {TextFormat.Count(report.Result.LargeOtherObjects)} large other objects tested against every target of their space; "
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

    private static IEnumerable<string> NpcLines(TooCloseSection report)
    {
        if (report.Result.Npc is { } summary) return StuckNpcLines(summary, report);
        return report.Options.Npcs == NpcHandling.Ignore
            ? [$"NPCs and creatures: ignored; {TextFormat.Count(report.Census.PlacedNpcs)} placed NPCs of other mods never cause removals."]
            : [];
    }

    private static IEnumerable<string> StuckNpcLines(NpcStuckSummary summary, TooCloseSection report)
    {
        var sizes = summary.Sizes;
        yield return $"NPCs and creatures (only when stuck in the object): {TextFormat.Count(sizes.Evaluated)} placed NPCs evaluated: "
            + $"{TextFormat.Count(sizes.ByBodyMesh)} sized by body mesh, {TextFormat.Count(sizes.ByObjectBounds)} by Object Bounds, "
            + $"{TextFormat.Count(sizes.ByHumanoidApproximation)} by humanoid approximation, {TextFormat.Count(sizes.ByPoint)} sized as a point; "
            + $"{TextFormat.Count(sizes.WithoutNpc)} skipped because their base is no NPC; "
            + $"{TextFormat.Count(summary.PairsTested)} NPC-object pairs tested ({TextFormat.Count(summary.CoreTests)} body boxes), "
            + $"{TextFormat.Count(summary.Conflicts)} objects with an NPC stuck in them.";
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
