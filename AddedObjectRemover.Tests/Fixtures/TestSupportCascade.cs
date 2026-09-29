using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The support cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestSupportCascade
{
    public sealed record Run(Ledger Ledger, ImmutableArray<Round> FollowUpRounds, AnchoringResult Anchoring);

    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        ShapeCatalog shapes,
        Protection protection,
        IReadOnlyList<int> seeds,
        ISolids solids,
        TerrainHeights terrain,
        float touchDistance,
        float threshold,
        int threads)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
        var visibility = TestSeededLedger.AllVisible(targets.Count);
        var seeded = TestSeededLedger.Seed(protection, targets.Count, seeds);
        var seedRound = seeded.Rounds[^1];
        var search = TouchSearch.Create(
            targets,
            visibility,
            protection.Groups.CollectReachableSpaces(targets, [.. seeded.RemovedIn(seedRound).Select(target => target.Index)]),
            excluded: [],
            shapes,
            new TriangleStore(shapes.ReadGeometry),
            touchDistance,
            options);
        var supporterFinder = new AnchoringSupporterFinder(targets, search, solids, shapes, touchDistance);
        var contactFinder = new AnchoringContactFinder(targets, search.MeshPaths, supporterFinder, terrain, search.Cache, touchDistance);
        var rule = new SupportRule(search, contactFinder, threshold, targets.Count, options, UntimedPhases.Instance);
        var (ledger, evidence) = Cascade.Run(seeded, rule);
        var followUpRounds = TestSeededLedger.FollowUpRounds(seeded, ledger);
        var report = TestSeededLedger.ReportOfFollowUpRounds(ledger);
        var anchoring = AnchoringOutcome.Create(ledger, report, followUpRounds, evidence);
        return new Run(ledger, followUpRounds, anchoring);
    }
}
