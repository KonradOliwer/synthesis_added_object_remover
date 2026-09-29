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
    IReadOnlyList<ObjectVisibility> visibility,
    BaseObjectShapeProvider shapes,
    Hosts hosts,
    InvisibleObjectReach reach,
    LeftoverConfig config)
{
    /// <param name="removedTargets">Target indices removed by the earlier steps.</param>
    public LeftoverResult SelectRemovals(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        return new LeftoverResult(EvaluateAll(removedTargets, options));
    }

    private LeftoverEvaluation[] EvaluateAll(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        var surroundings = VisibleTargetIndex.Build(targets, ObjectVisibility.VisibleIndices(visibility, except: new HashSet<int>()), shapes);
        var candidates = Enumerable.Range(0, targets.Count)
            .Where(index => visibility[index].Kind != null && !removedTargets.Contains(index))
            .ToArray();

        var evaluations = new LeftoverEvaluation[candidates.Length];
        Parallel.For(0, candidates.Length, options, i => evaluations[i] = Evaluate(candidates[i], surroundings, removedTargets));
        return evaluations;
    }

    private LeftoverEvaluation Evaluate(int targetIndex, VisibleTargetIndex surroundings, IReadOnlySet<int> removedTargets)
    {
        var target = targets[targetIndex];
        var kind = visibility[targetIndex].Kind!.Value;
        var radius = GetEffectiveRadius(target);
        var containingObject = hosts.HostOf(targetIndex);
        var areas = MeasureSurroundings(target, radius, surroundings, removedTargets);
        var ruleDecision = containingObject != null ? LeftoverDecision.RemovedInsideOtherObject : DecideByDirections(areas, config);
        return new LeftoverEvaluation(targetIndex, kind, radius, containingObject, areas, ApplyProtectedTypes(kind, ruleDecision), KeepReason: null);
    }

    private float GetEffectiveRadius(TargetObject target) =>
        reach.GetReach(target) is { } ownReach ? MathF.Min(config.SearchRadius, ownReach) : config.SearchRadius;

    private SectorAreas MeasureSurroundings(
        TargetObject target,
        float radius,
        VisibleTargetIndex surroundings,
        IReadOnlySet<int> removedTargets)
    {
        var areas = new SectorAreas(config.DirectionThresholdPercent);
        foreach (var neighbour in surroundings.FindAround(target.SpaceKey, target.Transform.Position, radius))
        {
            var removed = removedTargets.Contains(neighbour.TargetIndex);
            foreach (var sector in neighbour.Sectors) areas.Add(sector, neighbour.FootprintArea, removed);
        }
        return areas;
    }

    internal static LeftoverDecision DecideByDirections(SectorAreas areas, LeftoverConfig config)
    {
        var occupied = areas.OccupiedCount;
        if (occupied * Percent.PerWhole < config.OccupiedDirectionsPercent * SectorAreas.SectorCount) return LeftoverDecision.KeptTooFewSurroundingObjects;
        return areas.RemovedCount * Percent.PerWhole >= config.RemovedDirectionsPercent * occupied
            ? LeftoverDecision.RemovedSurroundingsRemoved
            : LeftoverDecision.KeptSurroundingsMostlyKept;
    }

    /// <summary>Protected types stay whatever the rules decided; referenced objects are held by the ledger.</summary>
    private LeftoverDecision ApplyProtectedTypes(InvisibleObjectKind kind, LeftoverDecision ruleDecision) =>
        ruleDecision.IsRemoval() && config.ProtectedKinds.Contains(kind) ? LeftoverDecision.KeptProtectedType : ruleDecision;
}
