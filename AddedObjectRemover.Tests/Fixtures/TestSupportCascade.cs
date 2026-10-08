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

        public ImmutableArray<AnchoringEvaluation> Evaluations => AnchoringRows.Join(Result);
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
        var input = new RestingObjectsInput(
            [.. targets],
            protection,
            objectsOfAnyPlugin,
            shapes,
            new TriangleStore(shapes.ReadTriangles),
            terrain,
            new Execution(threads));
        var result = TestRestingObjects.Run(seeded, input, new AlsoRemoveSettings(FollowUpRemovalMode.ObjectsSupportedByIt, touchDistance, threshold)).Result;
        return new Run(result.RemovalDecisions, result.Rounds) { Result = result };
    }
}
