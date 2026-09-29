namespace AddedObjectRemover;

/// <summary>
/// Final removal step: selects the target's invisible objects (critter spawners, sound and idle
/// markers, lights, trigger boxes, ...) that sit inside another mod's visible object or whose
/// surroundings, the target's visible objects around them, were removed. Every object is first
/// evaluated against the removals of the earlier steps only, and the removals are applied together
/// afterwards, so invisible objects never influence each other. The selector only proposes; the
/// ledger holds referenced objects and removes the linked groups of the removals.
/// </summary>
internal sealed class LeftoverInvisibleObjectSelector(
    IReadOnlyList<TargetObject> targets,
    TargetLooks looks,
    IVisibleTargets surroundings,
    Hosts hosts,
    InvisibleObjectReach reach,
    LeftoverOptions config,
    WorkOrder order)
{
    /// <param name="removedTargets">Target indices removed by the earlier steps.</param>
    public LeftoverResult SelectRemovals(IReadOnlySet<int> removedTargets, Execution execution)
    {
        return new LeftoverResult(EvaluateAll(removedTargets, execution));
    }

    private LeftoverEvaluation[] EvaluateAll(IReadOnlySet<int> removedTargets, Execution execution)
    {
        var candidates = Enumerable.Range(0, targets.Count)
            .Where(index => looks.ByTarget[index].Kind != null && !removedTargets.Contains(index))
            .ToArray();

        return ParallelMap.Run(
            execution,
            order.Among(candidates),
            () => new List<VisibleNeighbour>(),
            (position, neighbours) => Evaluate(candidates[position], removedTargets, neighbours));
    }

    /// <param name="neighbours">The calling worker's buffer.</param>
    private LeftoverEvaluation Evaluate(int targetIndex, IReadOnlySet<int> removedTargets, List<VisibleNeighbour> neighbours)
    {
        var target = targets[targetIndex];
        var kind = looks.ByTarget[targetIndex].Kind!.Value;
        var radius = GetEffectiveRadius(target);
        var containingObject = hosts.HostOf(targetIndex);
        var areas = MeasureSurroundings(target, radius, removedTargets, neighbours);
        var ruleDecision = containingObject != null ? LeftoverDecision.RemovedInsideOtherObject : DecideByDirections(areas, config);
        return new LeftoverEvaluation(targetIndex, kind, radius, containingObject, areas, ApplyProtectedTypes(kind, ruleDecision), KeepReason: null);
    }

    private float GetEffectiveRadius(TargetObject target) =>
        reach.GetReach(target) is { } ownReach ? MathF.Min(config.LookAround, ownReach) : config.LookAround;

    private SectorAreas MeasureSurroundings(
        TargetObject target,
        float radius,
        IReadOnlySet<int> removedTargets,
        List<VisibleNeighbour> neighbours)
    {
        var areas = new SectorAreas(config.DirectionClearedPercent);
        surroundings.Around(target.SpaceKey, target.Transform.Position, radius, neighbours);
        foreach (var neighbour in neighbours)
        {
            var removed = removedTargets.Contains(neighbour.Target.Index);
            foreach (var sector in neighbour.Sectors) areas.Add(sector, neighbour.FootprintArea, removed);
        }
        return areas;
    }

    internal static LeftoverDecision DecideByDirections(SectorAreas areas, LeftoverOptions config)
    {
        var occupied = areas.OccupiedCount;
        if (occupied * Percent.PerWhole < config.OccupiedDirectionsPercent * SectorAreas.SectorCount) return LeftoverDecision.KeptTooFewSurroundingObjects;
        return areas.RemovedCount * Percent.PerWhole >= config.ClearedDirectionsPercent * occupied
            ? LeftoverDecision.RemovedSurroundingsRemoved
            : LeftoverDecision.KeptSurroundingsMostlyKept;
    }

    /// <summary>Protected types stay whatever the rules decided; referenced objects are held by the ledger.</summary>
    private LeftoverDecision ApplyProtectedTypes(InvisibleObjectKind kind, LeftoverDecision ruleDecision) =>
        ruleDecision.IsRemoval() && config.NeverRemove.Contains(kind) ? LeftoverDecision.KeptProtectedType : ruleDecision;
}
