using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Final removal step: selects the target's invisible objects (critter spawners, sound and idle
/// markers, lights, trigger boxes, ...) that sit inside another mod's visible object or whose
/// surroundings, the target's visible objects around them, were removed. Every object is first
/// evaluated against the removals of the earlier steps only, and the removals are applied together
/// afterwards, so invisible objects never influence each other.
/// </summary>
internal sealed class LeftoverInvisibleObjectSelector(
    IReadOnlyList<TargetObject> targets,
    IReadOnlyList<ObjectVisibility> visibility,
    BaseObjectShapeProvider shapes,
    IReadOnlyDictionary<FormKey, OtherObjectIndex> otherObjects,
    ObjectContainment containment,
    InvisibleObjectReach reach,
    KeepReferencedRule keepRule,
    LeftoverConfig config)
{
    /// <param name="removedTargets">Target indices removed by the earlier steps.</param>
    public LeftoverResult SelectRemovals(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        var evaluations = EvaluateAll(removedTargets, options);
        return CollectResult(evaluations);
    }

    private LeftoverEvaluation[] EvaluateAll(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        var surroundings = VisibleTargetIndex.Build(targets, ObjectVisibility.VisibleIndices(visibility, except: new HashSet<int>()), shapes);
        var candidates = Enumerable.Range(0, targets.Count)
            .Where(index => visibility[index].Kind != null && !removedTargets.Contains(index))
            .ToArray();

        var evaluations = new LeftoverEvaluation[candidates.Length];
        Parallel.For(0, candidates.Length, options, i =>
        {
            evaluations[i] = Evaluate(candidates[i], surroundings, removedTargets);
        });
        return evaluations;
    }

    private LeftoverEvaluation Evaluate(int targetIndex, VisibleTargetIndex surroundings, IReadOnlySet<int> removedTargets)
    {
        var target = targets[targetIndex];
        var kind = visibility[targetIndex].Kind!.Value;
        var radius = GetEffectiveRadius(target);
        var containingObject = FindContainingOtherObject(target);
        var areas = MeasureSurroundings(target, radius, surroundings, removedTargets);
        var ruleDecision = containingObject != null ? LeftoverDecision.RemovedInsideOtherObject : DecideByDirections(areas);
        var (decision, keepReason) = ApplyKeepRules(target, kind, ruleDecision);
        return new LeftoverEvaluation(targetIndex, kind, radius, containingObject, areas, decision, keepReason);
    }

    private float GetEffectiveRadius(TargetObject target) =>
        reach.GetReach(target) is { } ownReach ? MathF.Min(config.SearchRadius, ownReach) : config.SearchRadius;

    /// <summary>Objects the target plugin replaced do not count, as everywhere else.</summary>
    private OtherObject? FindContainingOtherObject(TargetObject target)
    {
        var index = otherObjects[target.SpaceKey];
        var match = containment.FindContainingVisible(index, target.Transform.Position, skipReplaced: true);
        return match >= 0 ? index[match] : null;
    }

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

    private LeftoverDecision DecideByDirections(SectorAreas areas)
    {
        var occupied = areas.OccupiedCount;
        if (occupied * Percent.PerWhole < config.OccupiedDirectionsPercent * SectorAreas.SectorCount) return LeftoverDecision.KeptTooLittleScenery;
        return areas.RemovedCount * Percent.PerWhole >= config.RemovedDirectionsPercent * occupied
            ? LeftoverDecision.RemovedSurroundingsRemoved
            : LeftoverDecision.KeptSurroundingsMostlyKept;
    }

    /// <summary>Protected types and referenced objects stay whatever the rules decided.</summary>
    private (LeftoverDecision Decision, KeepReason? KeepReason) ApplyKeepRules(TargetObject target, InvisibleObjectKind kind, LeftoverDecision ruleDecision)
    {
        if (!ruleDecision.IsRemoval()) return (ruleDecision, null);
        if (config.ProtectedKinds.Contains(kind)) return (LeftoverDecision.KeptProtectedType, null);
        return keepRule.TryGetKeepReason(target, out var keepReason)
            ? (LeftoverDecision.KeptReferenced, keepReason)
            : (ruleDecision, null);
    }

    private static LeftoverResult CollectResult(IReadOnlyList<LeftoverEvaluation> evaluations)
    {
        var removals = evaluations
            .Where(evaluation => evaluation.IsRemoved)
            .Select(evaluation => new LeftoverRemoval(evaluation.TargetIndex, evaluation))
            .ToList();
        var kept = evaluations
            .Where(evaluation => evaluation.Decision == LeftoverDecision.KeptReferenced)
            .Select(evaluation => new KeptTarget(evaluation.TargetIndex, evaluation.KeepReason!, TouchedTargetIndex: null))
            .ToList();
        return new LeftoverResult(removals, kept, evaluations);
    }
}
