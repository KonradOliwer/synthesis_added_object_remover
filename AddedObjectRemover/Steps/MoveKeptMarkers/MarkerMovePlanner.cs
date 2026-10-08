using System.Collections.Immutable;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

internal static class MarkerMovePlanner
{
    /// <returns>The kept evaluations that sit inside another mod's object, in target order.</returns>
    public static ImmutableArray<LeftBehindCheck> Candidates(LeftBehindResult leftBehind, IRemovalDecisions final) =>
    [
        .. leftBehind.Evaluations.Where(evaluation => evaluation.ContainingObject != null && !final.IsRemoved(new TargetId(evaluation.TargetIndex))),
    ];

    public static MarkerMoves Plan(MarkerMoveInput input, MarkerMoveSettings options)
    {
        var objectsAroundMarker = new OtherObjectsAroundMarker(
            input.Surroundings.ObjectsOfAnyPlugin,
            input.Surroundings.Targets,
            input.Surroundings.Final.RemovedTargets().Select(target => target.Index).ToHashSet(),
            input.Shapes);
        var mover = new KeptMarkerMover(
            input.Targets,
            index => HomeCells.Find(input.Targets[index], input.Plugin),
            [
                new NavmeshSpotSearch(input.Navmeshes, objectsAroundMarker),
                new TerrainSpotSearch(input.Terrain, objectsAroundMarker),
            ],
            options.MaxDistance);
        return mover.Move(input.Candidates, input.Exec);
    }
}
