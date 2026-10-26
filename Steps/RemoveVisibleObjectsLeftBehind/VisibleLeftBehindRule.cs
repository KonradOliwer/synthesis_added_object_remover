using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>
/// Selects the target's visible objects whose surroundings, the target's other visible objects around them, were
/// removed. Every object is first evaluated against the removals of the earlier steps only, and the removals are
/// applied together afterwards, so these objects never influence each other. The rule only proposes; the removal
/// decisions keep referenced objects and remove the linked groups of the removals.
/// </summary>
internal sealed class VisibleLeftBehindRule(
    IReadOnlyList<TargetObject> targets,
    IBaseObjectShapes shapes,
    IVisibleTargetObjects surroundings,
    IBaseFacts bases,
    VisibleLeftBehindSettings config,
    WorkOrder order)
{
    /// <param name="decisions">The decisions of the earlier steps: their removals count as cleared, and the objects they decided are not evaluated again.</param>
    public VisibleLeftBehindResult SelectRemovals(IRemovalDecisions decisions, Execution execution)
    {
        var evaluations = EvaluateAll(decisions, execution);
        return new VisibleLeftBehindResult(StepOutcome.Performed, evaluations, VisibleLeftBehindRound.ProposalsOf(evaluations), Failure: null);
    }

    private VisibleLeftBehindCheck[] EvaluateAll(IRemovalDecisions decisions, Execution execution)
    {
        var candidates = Enumerable.Range(0, targets.Count)
            .Where(index => shapes.VisibilityOf(targets[index]).IsVisible && !decisions.IsDecided(targets[index].Id))
            .ToArray();

        return ParallelMap.RunOver(
            execution,
            order,
            candidates,
            () => new List<VisibleTargetNear>(),
            (targetIndex, neighbours) => Evaluate(targetIndex, decisions, neighbours),
            ParallelMap.AutomaticRangeSize);
    }

    /// <param name="neighbours">The calling worker's buffer.</param>
    private VisibleLeftBehindCheck Evaluate(int targetIndex, IRemovalDecisions decisions, List<VisibleTargetNear> neighbours)
    {
        var target = targets[targetIndex];
        var kind = VisibleObjectKinds.Of(target, bases);
        var areas = MeasureSurroundings(target, decisions, neighbours);
        return new VisibleLeftBehindCheck(targetIndex, kind, areas, Decide(areas, kind), KeepReason: null);
    }

    private SectorAreas MeasureSurroundings(TargetObject target, IRemovalDecisions decisions, List<VisibleTargetNear> neighbours)
    {
        surroundings.Around(target.SpaceKey, target.Transform.Position, config.LookAround, neighbours);
        // The neighbours come in ascending target id, which fixes the order of the float sums.
        return SectorAreaTally.Measure(
            neighbours
                .Where(neighbour => neighbour.Target != target.Id)
                .Select(neighbour => (neighbour.Box, decisions.IsRemoved(neighbour.Target))),
            target.Transform.Position,
            config.DirectionClearedPercent);
    }

    /// <summary>Protected kinds stay whatever the surroundings say; referenced objects are kept by the removal decisions.</summary>
    private VisibleLeftBehindOutcome Decide(SectorAreas areas, VisibleObjectKind kind) =>
        SectorRule.Decide(areas, config.MinOccupiedDirections, config.ClearedDirectionsPercent) switch
        {
            SectorVerdict.TooFewOccupiedDirections => VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects,
            SectorVerdict.TooFewClearedDirections => VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept,
            _ when config.NeverRemove.Contains(kind) => VisibleLeftBehindOutcome.KeptProtectedType,
            _ => VisibleLeftBehindOutcome.RemovedSurroundingsRemoved,
        };
}
