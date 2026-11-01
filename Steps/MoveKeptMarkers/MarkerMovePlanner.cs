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
    /// <param name="LeftBehind">Its evaluations the decisions kept, inside another mod's object, are the candidates.</param>
    /// <param name="Decisions">The decisions with the left-behind round, which the moves are added to.</param>
    /// <param name="OtherModsObjectsTakenAway">The other mods' objects pulled down (where they stand now) or removed (null), by id.</param>
    /// <param name="Caches">
    /// Index the visible target objects where they stand and the objects of any plugin as the run leaves them; their winning navmeshes
    /// of each target space, terrain and shapes are read.
    /// </param>
    /// <param name="Plugin">For the exterior cell each marker belongs to.</param>
    /// <param name="Settings">Null when kept markers stay where they are: the step is switched off.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        LeftBehindResult LeftBehind,
        IRunDecisions Decisions,
        IReadOnlyDictionary<OtherId, OtherObject?> OtherModsObjectsTakenAway,
        IRunCaches Caches,
        IPluginRecords Plugin,
        Execution Execution,
        MarkerMoveSettings? Settings);

    public static Decided<MarkerMoves> Plan(Input input)
    {
        var (decisions, caches) = (input.Decisions, input.Caches);
        var moves = Plan(
            input.Targets,
            input.LeftBehind,
            decisions,
            () => caches.VisibleObjectsOfAnyPluginAsTheyStand(input.OtherModsObjectsTakenAway),
            caches.IndexVisibleTargetsAsTheyStand(input.Targets, decisions),
            input.Plugin,
            caches.Navmeshes,
            caches.Terrain,
            caches.Shapes,
            input.Execution,
            input.Settings);
        return new Decided<MarkerMoves>(moves, decisions.AddMoves(moves.Moves));
    }

    /// <param name="leftBehind">Its evaluations the decisions kept, inside another mod's object, are the candidates.</param>
    /// <param name="final">The decisions with the left-behind round.</param>
    /// <param name="objectsOfAnyPlugin">
    /// As the run leaves them: those pulled down where they stand now, those removed left out. Asked only when the step is
    /// performed: the first ask builds the supporters' bounds index, which the closing statistics count.
    /// </param>
    /// <param name="plugin">For the exterior cell each marker belongs to.</param>
    /// <param name="navmeshes">The winning navmeshes of each target space.</param>
    /// <param name="settings">Null when kept markers stay where they are: the step is switched off.</param>
    /// <returns>An unexpected failure stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no moves.</returns>
    internal static MarkerMoves Plan(
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
                StepOutcome.StoppedByUnexpectedError, [], [], [], [], settings.MaxDistance, PartFailure.OfWholePart(MarkerMoveParts.MarkerMoves, failure)));
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
            .Where(evaluation => evaluation.ContainingObject != null && !final.HasLeftItsPlace(new TargetId(evaluation.TargetIndex))),
    ];
}
