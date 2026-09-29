using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    /// <param name="components">Required by the detailed log's statistics; the normal log never reads it.</param>
    /// <remarks>The kept lines come first, then the problems the phase met, where the statistics of the phase were printed.</remarks>
    public static LogSection Touch(
        World world,
        FollowUpResult followUp,
        TouchComponentSet? components,
        PhaseProblems problems,
        PhaseTimes times,
        TriangleStoreStats meshes,
        ReportContext context)
    {
        List<string> lines = [.. FollowUpKept(world, followUp, context), .. Problems(problems, context)];
        if (context.Detailed)
        {
            var stats = (components ?? throw new ArgumentNullException(nameof(components), "The detailed log needs the touch components.")).Stats;
            lines.Add(
                $"Touching objects: {CountRemovedBy<Cause.Touching>(followUp):N0} removed in {stats.Components:N0} components "
                + $"({stats.ComponentsWithRemovals:N0} with follow-up removals, largest {stats.LargestComponent:N0} removed objects, "
                + $"longest chain {stats.MaxDepth:N0} steps); {followUp.CountHeld():N0} kept as referenced.");
            lines.Add(FollowUpPairs(stats.Pairs, stats.Levels, "levels"));
            lines.Add(FollowUpMeshes(meshes));
            lines.Add(
                $"  Timing: setup {Describe.Seconds(times.Total(TimedPhase.TouchSetup))}, broad phase {Describe.Seconds(times.Total(TimedPhase.TouchBroadPhase))}, "
                + $"narrow phase {Describe.Seconds(times.Total(TimedPhase.TouchNarrowPhase))}.");
        }
        return new LogSection("touch", [.. lines]);
    }

    public static LogSection Anchoring(
        World world,
        FollowUpResult followUp,
        PhaseProblems problems,
        PhaseTimes times,
        TriangleStoreStats meshes,
        ReportContext context)
    {
        List<string> lines = [.. FollowUpKept(world, followUp, context), .. Problems(problems, context)];
        if (context.Detailed)
        {
            var work = followUp.Work;
            lines.Add(
                $"Anchoring: {CountRemovedBy<Cause.LostSupport>(followUp):N0} removed over {work.Rounds:N0} iterations; "
                + $"{work.Candidates:N0} touching objects evaluated ({work.Evaluations:N0} evaluations), "
                + $"{work.KeptWithoutContacts:N0} kept without any contact points, {followUp.CountHeld():N0} kept as referenced.");
            lines.Add(FollowUpPairs(work.Pairs, work.Rounds, "iterations"));
            lines.Add(FollowUpMeshes(meshes));
            lines.Add(
                $"  Timing: setup {Describe.Seconds(times.Total(TimedPhase.AnchoringSetup))}, "
                + $"touch search {Describe.Seconds(times.Total(TimedPhase.AnchoringTouchSearch))}, "
                + $"contact points {Describe.Seconds(times.Total(TimedPhase.AnchoringContactPoints))}.");
        }
        return new LogSection("anchoring", [.. lines]);
    }

    private static ImmutableArray<string> FollowUpKept(World world, FollowUpResult followUp, ReportContext context) =>
        Kept(world, followUp.Rounds.SelectMany(round => Decisions.KeptIn(followUp.Ledger, round)), context);

    /// <summary>The follow-up rounds' removals the rule itself decided; the linked group members removed with them are not counted.</summary>
    private static int CountRemovedBy<TCause>(FollowUpResult followUp) where TCause : Cause =>
        followUp.Rounds.Sum(round => followUp.Ledger.RemovedIn(round).Count(target => followUp.Ledger.Of(target)!.Cause is TCause));

    private static string FollowUpPairs(PairTestStats pairs, int rounds, string roundName) =>
        $"  Pairs: {pairs.PairsTested:N0} box candidates next to a removal tested over {rounds:N0} {roundName}, "
        + $"{pairs.TouchingPairs:N0} touching, {pairs.PairsWithoutGeometry:N0} without mesh triangles (never touching); "
        + $"{pairs.TrianglePairsTested:N0} triangle pairs tested exactly.";

    private static string FollowUpMeshes(TriangleStoreStats meshes) =>
        $"  Meshes indexed: {meshes.Built:N0} ({meshes.Triangles:N0} triangles), {meshes.Rebuilt:N0} rebuilt, "
        + $"{meshes.Evicted:N0} evicted"
        + (meshes.TooLarge > 0 ? $", {meshes.TooLarge:N0} over {MeshTriangleTree.MaxTriangles:N0} triangles not used" : string.Empty)
        + $"; peak resident {meshes.PeakResidentMeshes:N0} meshes, ~{meshes.PeakResidentBytes / BytesPerMebibyte:N0} MB.";
}
