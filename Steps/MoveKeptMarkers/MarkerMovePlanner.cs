using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

public static class MarkerMovePlanner
{
    /// <param name="LeftBehind">Its evaluations the decisions kept, inside another mod's object, are the candidates.</param>
    /// <param name="Decisions">The decisions with the left-behind round, which the moves are added to.</param>
    /// <param name="OtherModsObjects">What became of the other mods' objects: the markers' surroundings count one pulled down where it stands now and one removed not at all.</param>
    /// <param name="Caches">
    /// Index the visible target objects where they stand and the objects of any plugin as the run leaves them; their winning navmeshes
    /// of each target space, terrain and shapes are read.
    /// </param>
    /// <param name="Plugin">For the exterior cell each marker belongs to.</param>
    /// <param name="Settings">Kept markers stay where they are, and the step is switched off, unless <see cref="MarkerMoveSettings.Runs"/>.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        InvisibleLeftBehindResult LeftBehind,
        IRunDecisions Decisions,
        OtherModsObjectsResult OtherModsObjects,
        IRunCaches Caches,
        IPluginRecords Plugin,
        Execution Execution,
        MarkerMoveSettings Settings);

    public static Decided<MarkerMoves> Plan(Input input)
    {
        var moves = PlanMoves(input);
        return new Decided<MarkerMoves>(moves, input.Decisions.AddMoves(moves.Moves));
    }

    /// <remarks>
    /// The objects of any plugin are asked only when the step is performed: the first ask builds the supporters' bounds index, which
    /// the closing statistics count.
    /// </remarks>
    /// <returns>An unexpected failure stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no moves.</returns>
    private static MarkerMoves PlanMoves(Input input)
    {
        var settings = input.Settings;
        if (!settings.Runs) return MarkerMoves.SwitchedOff;
        if (input.LeftBehind.Outcome == StepOutcome.StoppedByUnexpectedError) return MarkerMoves.NotPerformedAfterLeftBehindStopped;

        var (targets, final, caches) = (input.Targets, input.Decisions, input.Caches);
        var visibleTargets = caches.IndexVisibleTargets(final.TargetsAsTheyStand(targets));
        return Failures.Attempt(
            () => new KeptMarkerMover(
                    targets,
                    index => HomeCells.Find(targets[index], input.Plugin),
                    SpotSearches(
                        caches.VisibleObjectsOfAnyPluginAsTheyStand(input.OtherModsObjects.TakenAway()), visibleTargets, final, caches.Shapes, caches.Navmeshes, caches.Terrain),
                    settings.MaxDistance)
                .Move(Candidates(input.LeftBehind, final), input.Execution),
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
    internal static ImmutableArray<InvisibleLeftBehindCheck> Candidates(InvisibleLeftBehindResult leftBehind, IRemovalDecisions final) =>
    [
        .. leftBehind.Evaluations
            .Where(evaluation => evaluation.ContainingObject != null && !final.HasLeftItsPlace(new TargetId(evaluation.TargetIndex))),
    ];
}
