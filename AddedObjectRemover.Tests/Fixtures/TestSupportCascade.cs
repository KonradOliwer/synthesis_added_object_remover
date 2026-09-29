using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The support cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestSupportCascade
{
    private static readonly Cause SeedCause = new Cause.TooClose(new OtherId(0));

    public sealed record Run(Ledger Ledger, ImmutableArray<Round> FollowUpRounds, AnchoringResult Anchoring);

    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        BaseObjectShapeProvider shapes,
        Protection protection,
        IReadOnlyList<int> seeds,
        SupporterIndex supporters,
        TerrainHeights terrain,
        float touchDistance,
        float threshold,
        int threads)
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
            excluded: [],
            shapes,
            new TriangleTreeCache(shapes.ReadGeometry),
            touchDistance,
            options);
        var supporterFinder = new AnchoringSupporterFinder(targets, search, supporters, shapes, touchDistance);
        var contactFinder = new AnchoringContactFinder(targets, search.MeshPaths, supporterFinder, terrain, search.Cache, touchDistance);
        var rule = new SupportRule(search, contactFinder, threshold, targets.Count, options);
        var (ledger, evidence) = Cascade.Run(seeded, rule);
        var followUpRounds = ledger.Rounds.RemoveRange(0, seeded.Rounds.Length);
        // Only the too-close round's causes read the world, and the report is asked for follow-up rounds only.
        var report = new LedgerReport(ledger, null!, LeftoverResult.None);
        var anchoring = AnchoringOutcome.Create(ledger, report, followUpRounds, evidence, rule, TimeSpan.Zero);
        return new Run(ledger, followUpRounds, anchoring);
    }
}
