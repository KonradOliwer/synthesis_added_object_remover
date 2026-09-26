namespace AddedObjectRemover;

internal enum OrphanDecision
{
    Removed,
    KeptNoSceneryNearby,
    KeptNotAllSidesCleared,
    KeptShareBelowThreshold,
    KeptReferenced,
}

/// <summary>One invisible target object checked for being left alone by removed scenery.</summary>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
internal sealed record OrphanEvaluation(int TargetIndex, QuadrantCounts Neighbours, OrphanDecision Decision, string? KeepReason);

/// <param name="Evaluations">In target order.</param>
internal sealed record OrphanResult(
    IReadOnlyList<OrphanRemoval> Removals,
    IReadOnlyList<KeptTarget> Kept,
    IReadOnlyList<OrphanEvaluation> Evaluations)
{
    public static OrphanResult None { get; } = new([], [], []);

    public int CountDecisions(OrphanDecision decision) => Evaluations.Count(evaluation => evaluation.Decision == decision);
}

/// <summary>
/// Final removal step: removes the target's invisible objects (critter spawners, sound and idle
/// markers, lights, trigger boxes, ...) once the target's visible scenery around them is gone.
/// Invisible objects never count as scenery, so a single pass decides all of them independently.
/// </summary>
internal sealed class OrphanedInvisibleObjectRemover
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly KeepReferencedRule _keepRule;
    private readonly float _radius;
    private readonly float _removedShareThreshold;

    public OrphanedInvisibleObjectRemover(
        IReadOnlyList<TargetObject> targets,
        BaseObjectShapeProvider shapes,
        KeepReferencedRule keepRule,
        float radius,
        float removedShareThreshold)
    {
        _targets = targets;
        _shapes = shapes;
        _keepRule = keepRule;
        _radius = radius;
        _removedShareThreshold = removedShareThreshold;
    }

    /// <param name="removedTargets">Target indices already removed by the earlier steps.</param>
    public OrphanResult Run(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        var isVisible = FindVisibleTargets(options);
        var scenery = VisibleTargetIndex.Build(_targets, isVisible, _shapes);
        var candidates = Enumerable.Range(0, _targets.Count)
            .Where(index => !isVisible[index] && !removedTargets.Contains(index))
            .ToArray();

        var evaluations = new OrphanEvaluation[candidates.Length];
        Parallel.For(0, candidates.Length, options, i =>
        {
            evaluations[i] = Evaluate(candidates[i], scenery, removedTargets);
        });
        return CollectResult(evaluations);
    }

    private bool[] FindVisibleTargets(ParallelOptions options)
    {
        var isVisible = new bool[_targets.Count];
        Parallel.For(0, _targets.Count, options, index =>
        {
            var target = _targets[index];
            isVisible[index] = _shapes.GetInvisibleReason(target.Base, target.IsPrimitive, target.HasMapMarker) == null;
        });
        return isVisible;
    }

    private OrphanEvaluation Evaluate(int targetIndex, VisibleTargetIndex scenery, IReadOnlySet<int> removedTargets)
    {
        var target = _targets[targetIndex];
        var neighbours = scenery.CountAround(target, _radius, removedTargets);
        var decision = DecideBySurroundings(neighbours);
        if (decision == OrphanDecision.Removed && _keepRule.TryGetKeepReason(target, out var keepReason))
        {
            return new OrphanEvaluation(targetIndex, neighbours, OrphanDecision.KeptReferenced, keepReason);
        }
        return new OrphanEvaluation(targetIndex, neighbours, decision, KeepReason: null);
    }

    private OrphanDecision DecideBySurroundings(QuadrantCounts neighbours)
    {
        if (neighbours.TotalVisible == 0) return OrphanDecision.KeptNoSceneryNearby;
        if (!neighbours.EveryOccupiedQuadrantHasRemoval) return OrphanDecision.KeptNotAllSidesCleared;
        return neighbours.RemovedShare >= _removedShareThreshold ? OrphanDecision.Removed : OrphanDecision.KeptShareBelowThreshold;
    }

    private static OrphanResult CollectResult(IReadOnlyList<OrphanEvaluation> evaluations)
    {
        var removals = evaluations
            .Where(evaluation => evaluation.Decision == OrphanDecision.Removed)
            .Select(evaluation => new OrphanRemoval(evaluation.TargetIndex, evaluation.Neighbours))
            .ToList();
        var kept = evaluations
            .Where(evaluation => evaluation.Decision == OrphanDecision.KeptReferenced)
            .Select(evaluation => new KeptTarget(evaluation.TargetIndex, evaluation.KeepReason!, TouchedTargetIndex: null))
            .ToList();
        return new OrphanResult(removals, kept, evaluations);
    }
}
