using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The touch cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestTouchCascade
{
    public sealed record Run(Ledger Ledger, ImmutableArray<Round> FollowUpRounds)
    {
        public required FollowUpResult Result { get; init; }

        public required TouchComponentSet Components { get; init; }

        /// <summary>Null unless diagnostics were collected.</summary>
        public required TouchExplanation? Explanation { get; init; }
    }

    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        ShapeCatalog shapes,
        Protection protection,
        IReadOnlyList<int> seeds,
        float touchDistance,
        int threads,
        bool collectDiagnostics)
    {
        var execution = new Execution(threads);
        var seeded = TestSeededLedger.Seed(protection, targets.Count, seeds);
        var triangles = new TriangleStore(shapes.ReadGeometry);
        var input = new FollowUpInput(
            [.. targets], TestSeededLedger.AllVisible(targets.Count), protection, Solids: null, shapes, triangles, NoTerrain(), execution, UntimedPhases.Instance);
        var result = FollowUp.Run(seeded, input, new FollowUpOptions(FollowUpRemovalMode.EverythingTouching, touchDistance, SupportLostFraction: 1f));
        return Explain(targets, shapes, triangles, result, execution, collectDiagnostics);
    }

    /// <param name="createRule">The rule deciding the follow-up rounds; the components are still found with the touch search.</param>
    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        ShapeCatalog shapes,
        Protection protection,
        IReadOnlyList<int> seeds,
        float touchDistance,
        int threads,
        bool collectDiagnostics,
        Func<TouchSearch, Execution, IFollowUpRule> createRule)
    {
        var execution = new Execution(threads);
        var seeded = TestSeededLedger.Seed(protection, targets.Count, seeds);
        var seedRound = seeded.Rounds[^1];
        var triangles = new TriangleStore(shapes.ReadGeometry);
        var search = TouchSearch.Create(
            targets,
            TestSeededLedger.AllVisible(targets.Count),
            protection.Groups.CollectReachableSpaces(targets, [.. seeded.RemovedIn(seedRound).Select(target => target.Index)]),
            excluded: [.. seeded.HeldIn(seedRound).Select(target => target.Index)],
            shapes,
            triangles,
            touchDistance,
            execution);
        var result = FollowUp.RunTouchRounds(seeded, search, createRule(search, execution));
        return Explain(targets, shapes, triangles, result, execution, collectDiagnostics);
    }

    private static Run Explain(
        IReadOnlyList<TargetObject> targets, ShapeCatalog shapes, TriangleStore triangles, FollowUpResult result, Execution execution, bool collectDiagnostics)
    {
        var components = FollowUp.Components(result, targets.Count, execution);
        // Touch explanations never read base facts.
        var explanation = collectDiagnostics
            ? Explanations.Compute(new ExplainInput(result, components, [.. targets], shapes, triangles, Bases: null!, execution)).Touch
            : null;
        return new Run(result.Ledger, result.Rounds)
        {
            Result = result,
            Components = components,
            Explanation = explanation,
        };
    }

    private static TerrainHeights NoTerrain() => new(new Dictionary<ExteriorCell, ILandscapeGetter>(), new Dictionary<FormKey, FormKey>());
}
