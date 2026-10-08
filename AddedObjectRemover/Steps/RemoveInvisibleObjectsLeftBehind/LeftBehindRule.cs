using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
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
    Hosts hosts,
    ReachRule reach,
    LeftBehindOptions config,
    WorkOrder order)
{
    /// <param name="removedTargets">Target indices removed by the earlier steps.</param>
    public LeftBehindResult SelectRemovals(IReadOnlySet<int> removedTargets, Execution execution)
    {
        return new LeftBehindResult(EvaluateAll(removedTargets, execution));
    }

    private LeftBehindCheck[] EvaluateAll(IReadOnlySet<int> removedTargets, Execution execution)
    {
        var candidates = Enumerable.Range(0, targets.Count)
            .Where(index => shapes.VisibilityOf(targets[index]).Kind != null && !removedTargets.Contains(index))
            .ToArray();

        return ParallelMap.RunOver(
            execution,
            order,
            candidates,
            () => new List<VisibleTargetNear>(),
            (targetIndex, neighbours) => Evaluate(targetIndex, removedTargets, neighbours),
            ParallelMap.AutomaticRangeSize);
    }

    /// <param name="neighbours">The calling worker's buffer.</param>
    private LeftBehindCheck Evaluate(int targetIndex, IReadOnlySet<int> removedTargets, List<VisibleTargetNear> neighbours)
    {
        var target = targets[targetIndex];
        var kind = shapes.VisibilityOf(target).Kind!.Value;
        var radius = GetEffectiveRadius(target);
        var containingObject = hosts.HostOf(targetIndex);
        var areas = MeasureSurroundings(target, radius, removedTargets, neighbours);
        var ruleDecision = containingObject != null ? LeftBehindOutcome.RemovedInsideOtherObject : DecideByDirections(areas, config);
        return new LeftBehindCheck(targetIndex, kind, radius, containingObject, areas, ApplyProtectedTypes(kind, ruleDecision), KeepReason: null);
    }

    private float GetEffectiveRadius(TargetObject target) =>
        reach.GetReach(target) is { } ownReach ? MathF.Min(config.LookAround, ownReach) : config.LookAround;

    private SectorAreas MeasureSurroundings(
        TargetObject target,
        float radius,
        IReadOnlySet<int> removedTargets,
        List<VisibleTargetNear> neighbours)
    {
        var areas = new SectorAreaTally(config.DirectionClearedPercent);
        surroundings.Around(target.SpaceKey, target.Transform.Position, radius, neighbours);
        // Ascending target id fixes the order of the float sums, so a share right at the threshold
        // is decided the same way whatever the spatial index's layout.
        foreach (var neighbour in neighbours)
        {
            var removed = removedTargets.Contains(neighbour.Target.Index);
            foreach (var sector in neighbour.Sectors) areas.Add(sector, neighbour.GroundArea, removed);
        }
        return areas.Build();
    }

    internal static LeftBehindOutcome DecideByDirections(SectorAreas areas, LeftBehindOptions config)
    {
        var occupied = areas.OccupiedCount;
        if (!SharePercent.AtLeast(occupied, CompassDirections.SectorCount, config.OccupiedDirectionsPercent)) return LeftBehindOutcome.KeptTooFewSurroundingObjects;
        return SharePercent.AtLeast(areas.RemovedCount, occupied, config.ClearedDirectionsPercent)
            ? LeftBehindOutcome.RemovedSurroundingsRemoved
            : LeftBehindOutcome.KeptSurroundingsMostlyKept;
    }

    /// <summary>Protected types stay whatever the rules decided; referenced objects are kept by the removal decisions.</summary>
    private LeftBehindOutcome ApplyProtectedTypes(InvisibleObjectKind kind, LeftBehindOutcome ruleDecision) =>
        LeftBehindOutcomes.IsRemoval(ruleDecision) && config.NeverRemove.Contains(kind) ? LeftBehindOutcome.KeptProtectedType : ruleDecision;
}
