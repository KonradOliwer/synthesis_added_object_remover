using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The support cascade over a scene of visible targets, seeded by a too-close round like the pipeline does.</summary>
internal static class TestSupportCascade
{
    public sealed record Run(IRemovalDecisions RemovalDecisions, ImmutableArray<Round> AlsoRemoveRounds)
    {
        public required RestingObjectsResult Result { get; init; }

        public IEnumerable<KeptObject> Kept => AlsoRemoveRounds.SelectMany(round => RemovalList.KeptIn(RemovalDecisions, round));

        public ImmutableArray<SupportEvaluationRow> Evaluations => SupportEvaluationRows.Join(Result, RemovalDecisions);
    }

    public static Run Execute(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        ObjectsToKeep protection,
        IReadOnlyList<int> seeds,
        IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin,
        TerrainHeights terrain,
        float touchDistance,
        float threshold,
        int threads)
    {
        var seeded = TestSeededDecisions.Seed(protection, targets.Count, seeds);
        var triangles = new TriangleStore(shapes.ReadTriangles);
        var input = new RoundsInput(
            [.. targets],
            protection,
            objectsOfAnyPlugin,
            shapes,
            triangles,
            TestSurfaceSamples.Over(triangles),
            terrain,
            new Execution(threads));
        var run = TestRestingObjects.Run(seeded, input, new AlsoRemoveSettings(FollowUpRemovalMode.OnlyWhatLosesItsSupport, touchDistance, threshold, TouchChainsShown: false));
        return new Run(run.Decisions, run.Result.Rounds) { Result = run.Result };
    }
}
