using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The support cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestSupportCascade
{
    public sealed record Run(Ledger Ledger, ImmutableArray<Round> FollowUpRounds)
    {
        public required FollowUpResult Result { get; init; }

        public IEnumerable<KeptTarget> Kept => FollowUpRounds.SelectMany(round => Decisions.KeptIn(Ledger, round));

        public ImmutableArray<AnchoringEvaluation> Evaluations => AnchoringRows.Join(Result);
    }

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
        var seeded = TestSeededLedger.Seed(protection, targets.Count, seeds);
        var input = new FollowUpInput(
            [.. targets],
            TestSeededLedger.AllVisible(targets.Count),
            protection,
            solids,
            shapes,
            new TriangleStore(shapes.ReadGeometry),
            terrain,
            new Execution(threads),
            UntimedPhases.Instance);
        var result = FollowUp.Run(seeded, input, new FollowUpOptions(FollowUpRemovalMode.ObjectsSupportedByIt, touchDistance, threshold));
        return new Run(result.Ledger, result.Rounds) { Result = result };
    }
}
