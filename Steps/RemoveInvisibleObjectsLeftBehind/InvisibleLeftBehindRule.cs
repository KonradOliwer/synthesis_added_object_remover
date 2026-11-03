using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>
/// Selects the target's invisible objects that sit inside another mod's visible object or whose surroundings, the target's visible
/// objects around them, were removed. Every object is evaluated against the removals of the earlier steps only and the removals are
/// applied together afterwards, so invisible objects never influence each other.
/// </summary>
internal sealed class InvisibleLeftBehindRule(
    IReadOnlyList<TargetObject> targets,
    IBaseObjectShapes shapes,
    IVisibleTargetObjects surroundings,
    ContainingObjects containingObjects,
    ReachRule reach,
    ISurroundingsJudging judging,
    ILeftBehindRoundJoin roundJoin,
    InvisibleLeftBehindSettings config,
    IKindsAlwaysKept kindsAlwaysKept,
    WorkOrder order)
{
    /// <param name="decisions">The decisions of the earlier steps: their removals count as cleared, and the objects they decided are not evaluated again.</param>
    public InvisibleLeftBehindResult SelectRemovals(IRemovalDecisions decisions, Execution execution)
    {
        var evaluations = EvaluateAll(decisions, execution);
        return new InvisibleLeftBehindResult(StepOutcome.Performed, evaluations, roundJoin.ProposalsOf(evaluations, ReasonOf), Failure: null);
    }

    private static RemovalReason ReasonOf(InvisibleLeftBehindCheck evaluation) =>
        evaluation.ContainingObject is { } container
            ? new InsideAnotherModsObject(container.Id)
            : new SurroundingsRemoved();

    private InvisibleLeftBehindCheck[] EvaluateAll(IRemovalDecisions decisions, Execution execution) =>
        ParallelMap.RunOver(
            execution,
            order,
            judging.Candidates(targets, decisions, target => shapes.VisibilityOf(target).Kind != null),
            () => new List<VisibleTargetNear>(),
            (targetIndex, neighbours) => Evaluate(targetIndex, decisions, neighbours),
            ParallelMap.AutomaticRangeSize);

    /// <param name="neighbours">The calling worker's buffer.</param>
    private InvisibleLeftBehindCheck Evaluate(int targetIndex, IRemovalDecisions decisions, List<VisibleTargetNear> neighbours)
    {
        var target = targets[targetIndex];
        var kind = shapes.VisibilityOf(target).Kind!.Value;
        var radius = GetEffectiveRadius(target);
        var containingObject = containingObjects.Of(targetIndex);
        var areas = MeasureSurroundings(target, radius, decisions, neighbours);
        var decision = judging.Judge(areas, config.Surroundings, isInsideAnotherModsObject: containingObject != null, isProtectedKind: kindsAlwaysKept.KindKeeping(target) != null);
        return new InvisibleLeftBehindCheck(targetIndex, kind, radius, containingObject, areas, decision, KeepReason: null);
    }

    private float GetEffectiveRadius(TargetObject target)
    {
        var lookAround = config.Surroundings.LookAround;
        return reach.GetReach(target) is { } ownReach ? MathF.Min(lookAround, ownReach) : lookAround;
    }

    private SectorAreas MeasureSurroundings(
        TargetObject target,
        float radius,
        IRemovalDecisions decisions,
        List<VisibleTargetNear> neighbours)
    {
        surroundings.Around(target.SpaceKey, target.Transform.Position, radius, neighbours);
        // The neighbours come in ascending target id, which fixes the order of the float sums.
        var boxes = neighbours.Select(neighbour => (neighbour.Box, decisions.FateOf(neighbour.Target)));
        return judging.MeasureSurroundings(boxes, target.Transform.Position, config.Surroundings.DirectionClearedPercent);
    }
}
