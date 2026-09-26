using System.Diagnostics;

namespace AddedObjectRemover;

internal enum LeftoverDecision
{
    Removed,
    KeptProtectedType,
    KeptNothingAround,
    KeptNearestKept,
    KeptNotAllSidesCleared,
    KeptShareBelowThreshold,
    KeptReferenced,
}

internal static class LeftoverDecisionText
{
    public static string Describe(LeftoverDecision decision) => decision switch
    {
        LeftoverDecision.Removed => "surroundings removed",
        LeftoverDecision.KeptProtectedType => "protected type",
        LeftoverDecision.KeptNothingAround => "no visible objects around",
        LeftoverDecision.KeptNearestKept => "nearest visible object kept",
        LeftoverDecision.KeptNotAllSidesCleared => "not all sides cleared",
        LeftoverDecision.KeptShareBelowThreshold => "removed share below threshold",
        LeftoverDecision.KeptReferenced => "referenced",
        _ => throw new UnreachableException($"Unknown leftover decision {decision}."),
    };
}

/// <summary>One invisible target object checked for being left behind by removed surroundings.</summary>
/// <param name="Surroundings">Empty for a protected type, which is not looked at.</param>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
internal sealed record LeftoverEvaluation(
    int TargetIndex,
    InvisibleObjectKind Kind,
    QuadrantCounts Surroundings,
    LeftoverDecision Decision,
    KeepReason? KeepReason);

/// <param name="Evaluations">In target order.</param>
internal sealed record LeftoverResult(
    IReadOnlyList<LeftoverRemoval> Removals,
    IReadOnlyList<KeptTarget> Kept,
    IReadOnlyList<LeftoverEvaluation> Evaluations)
{
    public static LeftoverResult None { get; } = new([], [], []);

    public int CountDecisions(LeftoverDecision decision) => Evaluations.Count(evaluation => evaluation.Decision == decision);
}

/// <summary>
/// Final removal step: selects the target's invisible objects (critter spawners, sound and idle
/// markers, lights, trigger boxes, ...) whose surroundings, the target's visible objects around
/// them, were removed. Invisible objects never count as surroundings, so a single pass decides
/// all of them independently.
/// </summary>
internal sealed class LeftoverInvisibleObjectSelector(
    IReadOnlyList<TargetObject> targets,
    IReadOnlyList<ObjectVisibility> visibility,
    BaseObjectShapeProvider shapes,
    KeepReferencedRule keepRule,
    IReadOnlySet<InvisibleObjectKind> protectedKinds,
    float radius,
    float removedShareThreshold)
{
    /// <param name="removedTargets">Target indices already removed by the earlier steps.</param>
    public LeftoverResult SelectRemovals(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        var surroundings = VisibleTargetIndex.Build(targets, visibility, shapes);
        var candidates = Enumerable.Range(0, targets.Count)
            .Where(index => visibility[index].Kind != null && !removedTargets.Contains(index))
            .ToArray();

        var evaluations = new LeftoverEvaluation[candidates.Length];
        Parallel.For(0, candidates.Length, options, i =>
        {
            evaluations[i] = Evaluate(candidates[i], surroundings, removedTargets);
        });
        return CollectResult(evaluations);
    }

    private LeftoverEvaluation Evaluate(int targetIndex, VisibleTargetIndex surroundings, IReadOnlySet<int> removedTargets)
    {
        var kind = visibility[targetIndex].Kind!.Value;
        if (protectedKinds.Contains(kind))
        {
            return new LeftoverEvaluation(targetIndex, kind, new QuadrantCounts(), LeftoverDecision.KeptProtectedType, KeepReason: null);
        }

        var target = targets[targetIndex];
        var neighbours = surroundings.FindAround(target, radius);
        var counts = CountByQuadrant(neighbours, removedTargets);
        var decision = DecideBySurroundings(neighbours, counts, removedTargets);
        if (decision == LeftoverDecision.Removed && keepRule.TryGetKeepReason(target, out var keepReason))
        {
            return new LeftoverEvaluation(targetIndex, kind, counts, LeftoverDecision.KeptReferenced, keepReason);
        }
        return new LeftoverEvaluation(targetIndex, kind, counts, decision, KeepReason: null);
    }

    private static QuadrantCounts CountByQuadrant(IEnumerable<VisibleNeighbour> neighbours, IReadOnlySet<int> removedTargets)
    {
        var counts = new QuadrantCounts();
        foreach (var neighbour in neighbours) counts.Add(neighbour.Quadrant, removedTargets.Contains(neighbour.TargetIndex));
        return counts;
    }

    private LeftoverDecision DecideBySurroundings(List<VisibleNeighbour> neighbours, QuadrantCounts counts, IReadOnlySet<int> removedTargets)
    {
        if (neighbours.Count == 0) return LeftoverDecision.KeptNothingAround;
        if (!IsNearestRemoved(neighbours, removedTargets)) return LeftoverDecision.KeptNearestKept;
        if (!counts.EveryOccupiedQuadrantHasRemoval) return LeftoverDecision.KeptNotAllSidesCleared;
        return counts.RemovedShare >= removedShareThreshold ? LeftoverDecision.Removed : LeftoverDecision.KeptShareBelowThreshold;
    }

    /// <summary>Of neighbours equally near, a kept one decides.</summary>
    private static bool IsNearestRemoved(List<VisibleNeighbour> neighbours, IReadOnlySet<int> removedTargets) =>
        neighbours
            .OrderBy(neighbour => neighbour.Distance)
            .ThenBy(neighbour => removedTargets.Contains(neighbour.TargetIndex))
            .Select(neighbour => removedTargets.Contains(neighbour.TargetIndex))
            .First();

    private static LeftoverResult CollectResult(IReadOnlyList<LeftoverEvaluation> evaluations)
    {
        var removals = evaluations
            .Where(evaluation => evaluation.Decision == LeftoverDecision.Removed)
            .Select(evaluation => new LeftoverRemoval(evaluation.TargetIndex, evaluation.Surroundings))
            .ToList();
        var kept = evaluations
            .Where(evaluation => evaluation.Decision == LeftoverDecision.KeptReferenced)
            .Select(evaluation => new KeptTarget(evaluation.TargetIndex, evaluation.KeepReason!, TouchedTargetIndex: null))
            .ToList();
        return new LeftoverResult(removals, kept, evaluations);
    }
}
