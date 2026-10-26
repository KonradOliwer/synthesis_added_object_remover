using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>
/// Final removal step: selects the target's invisible objects (critter spawners, sound and idle
/// markers, lights, trigger boxes, ...) that sit inside another mod's visible object or whose
/// surroundings, the target's visible objects around them, were removed. Every object is first
/// evaluated against the removals of the earlier steps only, and the removals are applied together
/// afterwards, so invisible objects never influence each other. The rule only proposes; the
/// removal decisions keep referenced objects and removes the linked groups of the removals.
/// </summary>
internal sealed class LeftBehindRule(
    IReadOnlyList<TargetObject> targets,
    IBaseObjectShapes shapes,
    IVisibleTargetObjects surroundings,
    ContainingObjects containingObjects,
    ReachRule reach,
    LeftBehindSettings config,
    WorkOrder order)
{
    /// <param name="decisions">The decisions of the earlier steps: their removals count as cleared, and the objects they decided are not evaluated again.</param>
    public LeftBehindResult SelectRemovals(IRemovalDecisions decisions, Execution execution)
    {
        var evaluations = EvaluateAll(decisions, execution);
        return new LeftBehindResult(StepOutcome.Performed, evaluations, LeftBehindRound.ProposalsOf(evaluations), Failure: null);
    }

    private LeftBehindCheck[] EvaluateAll(IRemovalDecisions decisions, Execution execution)
    {
        var candidates = Enumerable.Range(0, targets.Count)
            .Where(index => shapes.VisibilityOf(targets[index]).Kind != null && !decisions.IsDecided(targets[index].Id))
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
    private LeftBehindCheck Evaluate(int targetIndex, IRemovalDecisions decisions, List<VisibleTargetNear> neighbours)
    {
        var target = targets[targetIndex];
        var kind = shapes.VisibilityOf(target).Kind!.Value;
        var radius = GetEffectiveRadius(target);
        var containingObject = containingObjects.Of(targetIndex);
        var areas = MeasureSurroundings(target, radius, decisions, neighbours);
        var ruleDecision = containingObject != null ? LeftBehindOutcome.RemovedInsideOtherObject : DecideByDirections(areas, config);
        return new LeftBehindCheck(targetIndex, kind, radius, containingObject, areas, ApplyProtectedTypes(kind, ruleDecision), KeepReason: null);
    }

    private float GetEffectiveRadius(TargetObject target) =>
        reach.GetReach(target) is { } ownReach ? MathF.Min(config.LookAround, ownReach) : config.LookAround;

    private SectorAreas MeasureSurroundings(
        TargetObject target,
        float radius,
        IRemovalDecisions decisions,
        List<VisibleTargetNear> neighbours)
    {
        surroundings.Around(target.SpaceKey, target.Transform.Position, radius, neighbours);
        // The neighbours come in ascending target id, which fixes the order of the float sums.
        var boxes = neighbours.Select(neighbour => (neighbour.Box, decisions.IsRemoved(neighbour.Target)));
        return SectorAreaTally.Measure(boxes, target.Transform.Position, config.DirectionClearedPercent);
    }

    internal static LeftBehindOutcome DecideByDirections(SectorAreas areas, LeftBehindSettings config) =>
        SectorRule.Decide(areas, config.MinOccupiedDirections, config.ClearedDirectionsPercent) switch
        {
            SectorVerdict.TooFewOccupiedDirections => LeftBehindOutcome.KeptTooFewSurroundingObjects,
            SectorVerdict.TooFewClearedDirections => LeftBehindOutcome.KeptSurroundingsMostlyKept,
            _ => LeftBehindOutcome.RemovedSurroundingsRemoved,
        };

    /// <summary>Protected types stay whatever the rules decided; referenced objects are kept by the removal decisions.</summary>
    private LeftBehindOutcome ApplyProtectedTypes(InvisibleObjectKind kind, LeftBehindOutcome ruleDecision) =>
        LeftBehindOutcomes.IsRemoval(ruleDecision) && config.NeverRemove.Contains(kind) ? LeftBehindOutcome.KeptProtectedType : ruleDecision;
}
