using System.Diagnostics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal enum LeftoverDecision
{
    RemovedInsideOtherObject,
    RemovedSurroundingsRemoved,
    KeptTooLittleScenery,
    KeptSurroundingsMostlyKept,
    KeptProtectedType,
    KeptReferenced,
}

internal static class LeftoverDecisionText
{
    public static bool IsRemoval(LeftoverDecision decision) =>
        decision is LeftoverDecision.RemovedInsideOtherObject or LeftoverDecision.RemovedSurroundingsRemoved;

    public static string Describe(LeftoverDecision decision) => decision switch
    {
        LeftoverDecision.RemovedInsideOtherObject => "inside another mod's object",
        LeftoverDecision.RemovedSurroundingsRemoved => "surroundings removed",
        LeftoverDecision.KeptTooLittleScenery => "too little scenery around",
        LeftoverDecision.KeptSurroundingsMostlyKept => "surroundings mostly kept",
        LeftoverDecision.KeptProtectedType => "protected type",
        LeftoverDecision.KeptReferenced => "referenced",
        _ => throw new UnreachableException($"Unknown leftover decision {decision}."),
    };

    /// <summary>The decision's reason, with the linking record for a referenced object.</summary>
    public static string DescribeReason(LeftoverEvaluation evaluation)
    {
        var decision = Describe(evaluation.Decision);
        return evaluation.KeepReason is { } keepReason ? $"{decision}: {keepReason.Detail}" : decision;
    }
}

/// <summary>One invisible target object checked for being left behind.</summary>
/// <param name="Radius">The search radius used: the configured one, or the object's own smaller reach.</param>
/// <param name="ContainingObject">The visible other-mod object it sits inside; null when none.</param>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
internal sealed record LeftoverEvaluation(
    int TargetIndex,
    InvisibleObjectKind Kind,
    float Radius,
    OtherObject? ContainingObject,
    SectorAreas Surroundings,
    LeftoverDecision Decision,
    KeepReason? KeepReason)
{
    public bool IsRemoved => LeftoverDecisionText.IsRemoval(Decision);
}

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
    private const int PercentPerWhole = 100;

    /// <param name="removedTargets">Target indices removed by the earlier steps.</param>
    public LeftoverResult SelectRemovals(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        var evaluations = EvaluateAll(removedTargets, options);
        return CollectResult(evaluations);
    }

    private LeftoverEvaluation[] EvaluateAll(IReadOnlySet<int> removedTargets, ParallelOptions options)
    {
        var visibleTargets = Enumerable.Range(0, targets.Count).Where(index => visibility[index].IsVisible);
        var surroundings = VisibleTargetIndex.Build(targets, visibleTargets, shapes);
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
            areas.Add(neighbour.Sector, neighbour.FootprintArea, removedTargets.Contains(neighbour.TargetIndex));
        }
        return areas;
    }

    private LeftoverDecision DecideByDirections(SectorAreas areas)
    {
        var occupied = areas.OccupiedCount;
        if (occupied * PercentPerWhole < config.OccupiedDirectionsPercent * SectorAreas.SectorCount) return LeftoverDecision.KeptTooLittleScenery;
        return areas.RemovedCount * PercentPerWhole >= config.RemovedDirectionsPercent * occupied
            ? LeftoverDecision.RemovedSurroundingsRemoved
            : LeftoverDecision.KeptSurroundingsMostlyKept;
    }

    /// <summary>Protected types and referenced objects stay whatever the rules decided.</summary>
    private (LeftoverDecision Decision, KeepReason? KeepReason) ApplyKeepRules(TargetObject target, InvisibleObjectKind kind, LeftoverDecision ruleDecision)
    {
        if (!LeftoverDecisionText.IsRemoval(ruleDecision)) return (ruleDecision, null);
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
