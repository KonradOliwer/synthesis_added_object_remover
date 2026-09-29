using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The touch cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestTouchCascade
{
    public sealed record Run(Ledger Ledger, ImmutableArray<Round> FollowUpRounds, TouchClusters Clusters);

    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        ShapeCatalog shapes,
        Protection protection,
        IReadOnlyList<int> seeds,
        float touchDistance,
        int threads,
        bool collectDiagnostics) =>
        Execute(targets, shapes, protection, seeds, touchDistance, threads, collectDiagnostics, (search, options) => new TouchRule(search, options, UntimedPhases.Instance));

    /// <param name="createRule">The rule deciding the follow-up rounds; the components are still found with the touch search.</param>
    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        ShapeCatalog shapes,
        Protection protection,
        IReadOnlyList<int> seeds,
        float touchDistance,
        int threads,
        bool collectDiagnostics,
        Func<TouchSearch, ParallelOptions, IFollowUpRule> createRule)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
        var visibility = TestSeededLedger.AllVisible(targets.Count);
        var seeded = TestSeededLedger.Seed(protection, targets.Count, seeds);
        var seedRound = seeded.Rounds[^1];
        var search = TouchSearch.Create(
            targets,
            visibility,
            protection.Groups.CollectReachableSpaces(targets, [.. seeded.RemovedIn(seedRound).Select(target => target.Index)]),
            excluded: [.. seeded.HeldIn(seedRound).Select(target => target.Index)],
            shapes,
            new TriangleStore(shapes.ReadGeometry),
            touchDistance,
            options);
        var (ledger, evidence) = Cascade.Run(seeded, createRule(search, options));
        var followUpRounds = TestSeededLedger.FollowUpRounds(seeded, ledger);
        var report = TestSeededLedger.ReportOfFollowUpRounds(ledger);
        var clusters = TouchComponents.Find(
            ledger,
            report,
            targets.Count,
            seedRound,
            followUpRounds,
            evidence,
            search,
            options,
            UntimedPhases.Instance,
            collectDiagnostics);
        return new Run(ledger, followUpRounds, clusters);
    }
}
