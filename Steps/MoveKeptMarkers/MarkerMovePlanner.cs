using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

public static class MarkerMovePlanner
{
    /// <param name="leftBehind">Its evaluations the decisions kept, inside another mod's object, are the candidates.</param>
    /// <param name="final">The decisions with the left-behind round.</param>
    /// <param name="objectsOfAnyPlugin">Asked only when the step is performed: the first ask builds the supporters' bounds index, which the closing statistics count.</param>
    /// <param name="plugin">For the exterior cell each marker belongs to.</param>
    /// <param name="navmeshes">The winning navmeshes of each target space.</param>
    /// <param name="settings">Null when kept markers stay where they are: the step is switched off.</param>
    /// <returns>An unexpected failure stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no moves.</returns>
    public static MarkerMoves Plan(
        ImmutableArray<TargetObject> targets,
        LeftBehindResult leftBehind,
        IRemovalDecisions final,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
        IVisibleTargetObjects visibleTargets,
        IPluginRecords plugin,
        INavmeshes navmeshes,
        ITerrainHeights terrain,
        IBaseObjectShapes shapes,
        Execution execution,
        MarkerMoveSettings? settings)
    {
        if (settings == null) return MarkerMoves.SwitchedOff;
        if (leftBehind.Outcome == StepOutcome.StoppedByUnexpectedError) return MarkerMoves.NotPerformedAfterLeftBehindStopped;

        return Failures.Attempt(
            () => new KeptMarkerMover(
                    targets,
                    index => HomeCells.Find(targets[index], plugin),
                    SpotSearches(objectsOfAnyPlugin(), visibleTargets, final, shapes, navmeshes, terrain),
                    settings.MaxDistance)
                .Move(Candidates(leftBehind, final), execution),
            failure => new MarkerMoves(
                StepOutcome.StoppedByUnexpectedError, [], [], [], settings.MaxDistance, PartFailure.OfWholePart(MarkerMoveParts.MarkerMoves, failure)));
    }

    private static IFreeSpotSearch[] SpotSearches(
        IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin,
        IVisibleTargetObjects visibleTargets,
        IRemovalDecisions final,
        IBaseObjectShapes shapes,
        INavmeshes navmeshes,
        ITerrainHeights terrain)
    {
        var objectsAroundMarker = new OtherObjectsAroundMarker(
            objectsOfAnyPlugin,
            visibleTargets,
            final.RemovedTargets().Select(target => target.Index).ToHashSet(),
            shapes);
        return
        [
            new NavmeshSpotSearch(navmeshes, objectsAroundMarker),
            new TerrainSpotSearch(terrain, objectsAroundMarker),
        ];
    }

    /// <returns>The kept evaluations that sit inside another mod's object, in target order.</returns>
    internal static ImmutableArray<LeftBehindCheck> Candidates(LeftBehindResult leftBehind, IRemovalDecisions final) =>
    [
        .. leftBehind.Evaluations
            .Where(evaluation => evaluation.ContainingObject != null && !final.IsRemoved(new TargetId(evaluation.TargetIndex))),
    ];
}
