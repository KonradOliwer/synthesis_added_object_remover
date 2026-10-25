using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The section of the also-remove step's mode; the empty one when no round ran, and the not-performed line when the step was stopped.</summary>
    /// <remarks>Rounds run exactly when the step was performed and the too-close round removed something.</remarks>
    /// <param name="decisions">The run's decisions with the also-remove rounds applied.</param>
    public static LogSection BuildAlsoRemoveSection(
        CollectedObjects world, RestingObjectsResult alsoRemove, IRemovalDecisions decisions, TriangleStoreStats meshes, ReportContext context) =>
        alsoRemove.Outcome switch
        {
            StepOutcome.StoppedByUnexpectedError => BuildFollowUpSection([BuildNotPerformedLine("Also remove")]),
            StepOutcome.SwitchedOff => BuildNoFollowUpSection(),
            StepOutcome.Performed => alsoRemove.HadSeeds
                ? BuildModeSection(world, alsoRemove, decisions, alsoRemove.RoundsTime, meshes, context)
                : BuildNoFollowUpSection(),
            _ => throw new UnreachableException($"Unexpected also-remove outcome {alsoRemove.Outcome}."),
        };

    /// <summary>The section of the rounds that ran: the touch section or the anchoring section, by the step's mode.</summary>
    /// <param name="decisions">The run's decisions with the also-remove rounds applied.</param>
    public static LogSection BuildModeSection(
        CollectedObjects world,
        RestingObjectsResult alsoRemove,
        IRemovalDecisions decisions,
        TimeSpan elapsed,
        TriangleStoreStats meshes,
        ReportContext context) =>
        alsoRemove.Mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt =>
                AlsoRemoveSection("touch", world, alsoRemove, decisions, meshes, elapsed, context, () => TouchChainLines(alsoRemove, decisions)),
            FollowUpRemovalMode.OnlyWhatLosesItsSupport =>
                AlsoRemoveSection("anchoring", world, alsoRemove, decisions, meshes, elapsed, context, () => SupportLines(world, alsoRemove, decisions)),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {alsoRemove.Mode}."),
        };

    /// <remarks>Left out when the chain search failed; otherwise the chains must be there.</remarks>
    private static string[] TouchChainLines(RestingObjectsResult alsoRemove, IRemovalDecisions decisions)
    {
        if (alsoRemove.TouchChainsFailure is not null) return [];

        var stats = (alsoRemove.TouchChains ?? throw new InvalidOperationException("The touch rounds had seeds and their chain search did not fail, yet no touch chains were given.")).Stats;
        return
        [
            $"Touching objects: {TextFormat.Count(CountRemovedBy<RemovalReason.Touching>(alsoRemove, decisions))} removed in {TextFormat.Count(stats.Components)} components "
            + $"({TextFormat.Count(stats.ComponentsWithFollowUpRemovals)} with follow-up removals, largest {TextFormat.Count(stats.LargestComponent)} removed objects, "
            + $"longest chain {TextFormat.Count(stats.MaxDepth)} steps); {TextFormat.Count(CountKept(alsoRemove, decisions))} kept as referenced.",
            AlsoRemovePairs(stats.Pairs, stats.Levels, "levels"),
        ];
    }

    private static string[] SupportLines(CollectedObjects world, RestingObjectsResult alsoRemove, IRemovalDecisions decisions)
    {
        var work = alsoRemove.Work;
        return
        [
            $"Anchoring: {TextFormat.Count(CountRemovedBy<RemovalReason.LostSupport>(alsoRemove, decisions))} removed over {TextFormat.Count(work.Rounds)} iterations; "
            + $"{TextFormat.Count(work.Candidates)} touching objects evaluated ({TextFormat.Count(work.Evaluations)} evaluations), "
            + $"{TextFormat.Count(work.KeptWithoutContacts)} kept without any contact points, {TextFormat.Count(CountKept(alsoRemove, decisions))} kept as referenced.",
            AlsoRemovePairs(work.Pairs, work.Rounds, "iterations"),
            .. ZeroWeightLines(world, alsoRemove),
        ];
    }

    /// <param name="statistics">The detailed log's statistics of the rule; only called for the detailed log.</param>
    private static LogSection AlsoRemoveSection(
        string id,
        CollectedObjects world,
        RestingObjectsResult alsoRemove,
        IRemovalDecisions decisions,
        TriangleStoreStats meshes,
        TimeSpan elapsed,
        ReportContext context,
        Func<string[]> statistics)
    {
        List<string> lines = [.. BuildKeptLines(world, alsoRemove.Rounds.SelectMany(round => RemovalList.KeptIn(decisions, round)))];
        if (context.Detailed) lines.AddRange([.. statistics(), AlsoRemoveMeshes(meshes), AlsoRemoveTime(alsoRemove, elapsed)]);
        return new LogSection(id, [.. lines]);
    }

    /// <summary>The also-remove rounds' removals the rule itself decided; the linked group members removed with them are not counted.</summary>
    private static int CountRemovedBy<TReason>(RestingObjectsResult alsoRemove, IRemovalDecisions decisions) where TReason : RemovalReason =>
        alsoRemove.Rounds.Sum(round => decisions.RemovedIn(round).Count(target => decisions.Of(target)!.Reason is TReason));

    /// <summary>The objects the also-remove rounds kept.</summary>
    private static int CountKept(RestingObjectsResult alsoRemove, IRemovalDecisions decisions) =>
        alsoRemove.Rounds.Sum(round => decisions.KeptIn(round).Length);

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
