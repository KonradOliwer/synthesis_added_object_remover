using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The touch cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestTouchCascade
{
    private static readonly Cause SeedCause = new Cause.TooClose(new OtherId(0));

    public sealed record Run(Ledger Ledger, ImmutableArray<Round> FollowUpRounds, TouchClusters Clusters);

    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        BaseObjectShapeProvider shapes,
        Protection protection,
        IReadOnlyList<int> seeds,
        float touchDistance,
        int threads,
        bool collectDiagnostics)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
        var visibility = Enumerable.Repeat(ObjectVisibility.Visible, targets.Count).ToArray();
        var start = Ledger.Start(protection, targets.Count);
        var seeded = start.Apply(RoundKind.TooClose, [.. seeds.Select(seed => new Proposal(new TargetId(seed), SeedCause))]);
        var seedRound = seeded.Rounds[^1];
        var search = TouchSearch.Create(
            targets,
            visibility,
            protection.Groups.CollectReachableSpaces(targets, [.. seeded.RemovedIn(seedRound).Select(target => target.Index)]),
            excluded: [.. seeded.HeldIn(seedRound).Select(target => target.Index)],
            shapes,
            new TriangleTreeCache(shapes.ReadGeometry),
            touchDistance,
            options);
        var rule = new TouchRule(search, options);
        var (ledger, evidence) = Cascade.Run(seeded, rule);
        var followUpRounds = ledger.Rounds.RemoveRange(0, seeded.Rounds.Length);
        // Only the too-close round's causes read the world, and the report is asked for follow-up rounds only.
        var report = new LedgerReport(ledger, null!, LeftoverResult.None);
        var clusters = TouchComponents.Find(
            ledger,
            report,
            targets.Count,
            seedRound,
            followUpRounds,
            evidence,
            search,
            search.Tester.GetStats(),
            new TouchTimes(TimeSpan.Zero, rule.BroadPhase, rule.NarrowPhase),
            options,
            collectDiagnostics);
        return new Run(ledger, followUpRounds, clusters);
    }
}
