using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <param name="BoxIndex">How long building the shape-zone box index took; zero for the BoundingBox zone.</param>
/// <param name="Search">How long the search itself took; zero for the BoundingBox zone.</param>
internal readonly record struct TooCloseTimes(TimeSpan Total, TimeSpan BoxIndex, TimeSpan Search);

/// <summary>The mesh and NPC-body cache statistics read when the too-close phase ended.</summary>
/// <param name="EffectOnlyMeshes">Meshes of fog, light rays and the like, which never make anything too close.</param>
internal sealed record ClashPerf(TriangleStoreStats Meshes, NpcBodyPerf Bodies, int EffectOnlyMeshes);

/// <param name="Kept">The objects the too-close round left standing, in round order.</param>
internal sealed record TooCloseReport(
    World World,
    ModKey Target,
    ClashOptions Options,
    ClashResult Result,
    RivalCensus Census,
    ImmutableArray<KeptTarget> Kept,
    PhaseProblems Problems,
    ClashPerf Perf,
    TooCloseTimes Times);

internal static partial class LogSections
{
    /// <remarks>Mesh problems come right after the shape-zone statistics, the phase's own meshes, and before the summary.</remarks>
    public static LogSection TooClose(TooCloseReport report, ReportContext context) =>
        new("tooClose",
        [
            .. context.Detailed && report.Options.Zone == ZoneShape.ObjectShape ? ShapeZoneLines(report) : [],
            .. Problems(report.Problems, context),
            $"Found {report.Result.Hits.Length:N0} of {report.World.Targets.Length:N0} {report.Target} objects too close to other mods' objects"
                + $"{Describe.TimedSuffix(report.Times.Total, context)}.",
            .. context.Detailed ? InvisibleOthers(report.Census) : [],
            .. context.Detailed ? NpcLines(report) : [],
            .. Kept(report.World, report.Kept, context),
        ]);

    private static ImmutableArray<string> ShapeZoneLines(TooCloseReport report)
    {
        var stats = report.Result.Work.Zone;
        var meshes = report.Perf.Meshes;
        return
        [
            $"Object-shape zones: {stats.CandidatePairs:N0} candidate pairs, {stats.BoxFilterPasses:N0} passed the box filter, "
                + $"{stats.NarrowTests:N0} mesh tests, {stats.Hits:N0} hits, {stats.CentrePointFallbacks:N0} centre-point fallbacks "
                + $"(other object without mesh triangles), {stats.BoxZoneTargets:N0} targets without mesh triangles used their box; "
                + $"{stats.TrianglePairsTested:N0} triangle pairs tested exactly.",
            $"  {report.Result.LargeRivals:N0} large other objects tested against every target of their space; "
                + $"{report.Perf.EffectOnlyMeshes:N0} effect-only meshes (fog, light rays, water spray) ignored.",
            $"  Meshes indexed: {meshes.Built:N0} ({meshes.Triangles:N0} triangles), peak resident ~{meshes.PeakResidentBytes / BytesPerMebibyte:N0} MB; "
                + $"timing: box index {Describe.Seconds(report.Times.BoxIndex)}, search {Describe.Seconds(report.Times.Search)}.",
        ];
    }

    private static IEnumerable<string> InvisibleOthers(RivalCensus census)
    {
        var total = census.InvisibleByReason.Sum(entry => entry.Value);
        if (total == 0) yield break;
        yield return $"  Ignored {total:N0} other-mod objects that are invisible (markers, lights, sounds, decals, trigger boxes, ...).";
        foreach (var (reason, count) in census.InvisibleByReason) yield return $"    {reason}: {count:N0}";
    }

    private static IEnumerable<string> NpcLines(TooCloseReport report)
    {
        if (report.Result.Npc is { } summary) return StuckNpcLines(summary, report.Perf.Bodies);
        return report.Options.Npcs == NpcHandling.Ignore
            ? [$"NPCs and creatures: ignored; {report.Census.PlacedNpcs:N0} placed NPCs of other mods never cause removals."]
            : [];
    }

    private static IEnumerable<string> StuckNpcLines(NpcStuckSummary summary, NpcBodyPerf bodies)
    {
        var cache = bodies.Cache;
        var sizes = summary.Sizes;
        yield return $"NPCs and creatures (only when stuck in the object): {sizes.Evaluated:N0} placed NPCs evaluated: "
            + $"{sizes.ByBodyMesh:N0} sized by body mesh, {sizes.ByObjectBounds:N0} by Object Bounds, "
            + $"{sizes.ByHumanoidApproximation:N0} by humanoid approximation, {sizes.ByPoint:N0} sized as a point; "
            + $"{sizes.WithoutNpc:N0} skipped because their base is no NPC; "
            + $"{summary.PairsTested:N0} NPC-object pairs tested ({summary.CoreTests:N0} body boxes), "
            + $"{summary.Conflicts:N0} objects with an NPC stuck in them.";
        yield return $"  NPC body cache: {cache.BodiesBuilt:N0} bodies built, {cache.BodiesReused:N0} reused; "
            + $"{cache.BasesResolved:N0} NPC bases resolved, {cache.BasesReused:N0} reused; "
            + $"{cache.ListsResolved:N0} leveled lists resolved, {cache.ListsReused:N0} reused; "
            + $"{bodies.BodyMeshSetsMeasured:N0} body mesh sets measured.";
        foreach (var fallback in summary.PointFallbacks)
        {
            yield return $"  Sized as a point {Describe.OtherObject(fallback.Npc)}: {fallback.Reason}.";
        }
    }
}
