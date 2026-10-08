using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <param name="touchChains">Gives the detailed log's chain statistics; the normal log never reads it, and the detailed log leaves those lines out when it is null (the search for them failed).</param>
    /// <remarks>The kept lines come first, then the problems the phase met, where the statistics of the phase were printed.</remarks>
    public static LogSection Touch(
        CollectedObjects world,
        RestingObjectsResult alsoRemove,
        TouchChainSet? touchChains,
        PhaseProblems problems,
        TimeSpan elapsed,
        TriangleStoreStats meshes,
        ReportContext context)
    {
        return AlsoRemoveSection(
            "touch", world, alsoRemove, problems, meshes, elapsed, context,
            () => touchChains is { } chains ? TouchChainLines(alsoRemove, chains.Stats) : []);
    }

    private static string[] TouchChainLines(RestingObjectsResult alsoRemove, TouchChainStatistics stats) =>
    [
        $"Touching objects: {TextFormat.Count(CountRemovedBy<RemovalReason.Touching>(alsoRemove))} removed in {TextFormat.Count(stats.Components)} components "
        + $"({TextFormat.Count(stats.ComponentsWithRemovals)} with follow-up removals, largest {TextFormat.Count(stats.LargestComponent)} removed objects, "
        + $"longest chain {TextFormat.Count(stats.MaxDepth)} steps); {TextFormat.Count(alsoRemove.CountKept())} kept as referenced.",
        AlsoRemovePairs(stats.Pairs, stats.Levels, "levels"),
    ];

    public static LogSection Anchoring(
        CollectedObjects world,
        RestingObjectsResult alsoRemove,
        PhaseProblems problems,
        TimeSpan elapsed,
        TriangleStoreStats meshes,
        ReportContext context)
    {
        return AlsoRemoveSection("anchoring", world, alsoRemove, problems, meshes, elapsed, context, () => AnchoringLines(alsoRemove));
    }

    private static string[] AnchoringLines(RestingObjectsResult alsoRemove)
    {
        var work = alsoRemove.Work;
        return
        [
            $"Anchoring: {TextFormat.Count(CountRemovedBy<RemovalReason.LostSupport>(alsoRemove))} removed over {TextFormat.Count(work.Rounds)} iterations; "
            + $"{TextFormat.Count(work.Candidates)} touching objects evaluated ({TextFormat.Count(work.Evaluations)} evaluations), "
            + $"{TextFormat.Count(work.KeptWithoutContacts)} kept without any contact points, {TextFormat.Count(alsoRemove.CountKept())} kept as referenced.",
            AlsoRemovePairs(work.Pairs, work.Rounds, "iterations"),
        ];
    }

    /// <param name="statistics">The detailed log's statistics of the rule; only called for the detailed log.</param>
    private static LogSection AlsoRemoveSection(
        string id,
        CollectedObjects world,
        RestingObjectsResult alsoRemove,
        PhaseProblems problems,
        TriangleStoreStats meshes,
        TimeSpan elapsed,
        ReportContext context,
        Func<string[]> statistics)
    {
        var lines = KeptThenProblems(AlsoRemoveKept(world, alsoRemove, context), problems, context);
        if (context.Detailed) lines.AddRange([.. statistics(), AlsoRemoveMeshes(meshes), AlsoRemoveTime(alsoRemove, elapsed)]);
        return new LogSection(id, [.. lines]);
    }

    private static ImmutableArray<string> AlsoRemoveKept(CollectedObjects world, RestingObjectsResult alsoRemove, ReportContext context) =>
        Kept(world, alsoRemove.Rounds.SelectMany(round => RemovalList.KeptIn(alsoRemove.RemovalDecisions, round)), context);

    /// <summary>The also-remove rounds' removals the rule itself decided; the linked group members removed with them are not counted.</summary>
    private static int CountRemovedBy<TReason>(RestingObjectsResult alsoRemove) where TReason : RemovalReason =>
        alsoRemove.Rounds.Sum(round => alsoRemove.RemovalDecisions.RemovedIn(round).Count(target => alsoRemove.RemovalDecisions.Of(target)!.Reason is TReason));

    private static string AlsoRemoveTime(RestingObjectsResult alsoRemove, TimeSpan elapsed) =>
        $"Also remove: {TextFormat.Count(alsoRemove.Rounds.Length)} rounds in {TextFormat.Seconds(elapsed)}.";

    private static string AlsoRemovePairs(PairTestStats pairs, int rounds, string roundName) =>
        $"  Pairs: {TextFormat.Count(pairs.PairsTested)} box candidates next to a removal tested over {TextFormat.Count(rounds)} {roundName}, "
        + $"{TextFormat.Count(pairs.TouchingPairs)} touching, {TextFormat.Count(pairs.PairsWithoutGeometry)} without mesh triangles (never touching); "
        + $"{TextFormat.Count(pairs.TrianglePairsTested)} triangle pairs tested exactly.";

    private static string AlsoRemoveMeshes(TriangleStoreStats meshes) =>
        $"  Meshes indexed: {TextFormat.Count(meshes.Built)} ({TextFormat.Count(meshes.Triangles)} triangles)"
        + (meshes.TooLarge > 0 ? $", {TextFormat.Count(meshes.TooLarge)} over {TextFormat.Count(MeshLimits.Tree.MaxIndexedTriangles)} triangles not used" : string.Empty)
        + ".";
}
