using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The touch cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestTouchCascade
{
    public sealed record Run(IRemovalDecisions RemovalDecisions, ImmutableArray<Round> AlsoRemoveRounds)
    {
        public required RestingObjectsResult Result { get; init; }

        public required TouchChainSet TouchChains { get; init; }

        /// <summary>Null unless diagnostics were collected.</summary>
        public required TouchChainEdges? Explanation { get; init; }
    }

    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        ObjectsToKeep protection,
        IReadOnlyList<int> seeds,
        float touchDistance,
        int threads,
        bool collectDiagnostics)
    {
        var execution = new Execution(threads);
        var seeded = TestSeededDecisions.Seed(protection, targets.Count, seeds);
        var triangles = new TriangleStore(shapes.ReadTriangles);
        var input = new RestingObjectsInput(
            [.. targets], protection, ObjectsOfAnyPlugin: null, shapes, triangles, NoTerrain(), execution);
        var result = TestRestingObjects.Run(seeded, input, new AlsoRemoveSettings(FollowUpRemovalMode.EverythingTouching, touchDistance, SupportLostFraction: 1f));
        return Explain(targets, shapes, triangles, result, execution, collectDiagnostics);
    }

    /// <param name="createRule">The rule deciding the also-remove rounds; the touch chains are still found with the touch search.</param>
    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        ObjectsToKeep protection,
        IReadOnlyList<int> seeds,
        float touchDistance,
        int threads,
        bool collectDiagnostics,
        Func<TouchSearch, Execution, IRemovalRounds<RoundDetails>> createRule)
    {
        var execution = new Execution(threads);
        var seeded = TestSeededDecisions.Seed(protection, targets.Count, seeds);
        var seedRound = seeded.Rounds[^1];
        var triangles = new TriangleStore(shapes.ReadTriangles);
        var search = TouchSearch.Create(
            targets,
            ReachableSpaces.CollectReachableSpaces(protection.Groups, targets, [.. seeded.RemovedIn(seedRound).Select(target => target.Index)]),
            excluded: [.. seeded.KeptIn(seedRound).Select(target => target.Index)],
            shapes,
            triangles,
            touchDistance,
            execution);
        var plan = new RestingObjectsPlan(FollowUpRemovalMode.EverythingTouching, HadSeeds: true, createRule(search, execution), search);
        return Explain(targets, shapes, triangles, TestRestingObjects.Run(seeded, plan), execution, collectDiagnostics);
    }

    private static Run Explain(
        IReadOnlyList<TargetObject> targets, IBaseObjectShapes shapes, TriangleStore triangles, RestingObjectsRun restingObjects, Execution execution, bool collectDiagnostics)
    {
        var result = restingObjects.Result;
        var touchChains = RestingObjects.FindTouchChains(restingObjects, targets.Count, execution);
        // Touch explanations never read base facts.
        var explanation = collectDiagnostics
            ? ExplanationFinder.Compute(new ReportFileDetailsInput(restingObjects, touchChains, [.. targets], shapes, triangles, Bases: null!, execution)).Touch
            : null;
        return new Run(result.RemovalDecisions, result.Rounds)
        {
            Result = result,
            TouchChains = touchChains,
            Explanation = explanation,
        };
    }

    private static TerrainHeights NoTerrain() =>
        TestGround.NoTerrain();
}
