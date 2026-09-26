using System.Diagnostics;

namespace AddedObjectRemover;

internal enum LeftoverDecision
{
    RemovedInsideOtherObject,
    RemovedSurroundingsRemoved,
    KeptTooFewSurroundingObjects,
    KeptSurroundingsMostlyKept,
    KeptProtectedType,
    KeptReferenced,
}

internal static class LeftoverDecisions
{
    public static bool IsRemoval(this LeftoverDecision decision) =>
        decision is LeftoverDecision.RemovedInsideOtherObject or LeftoverDecision.RemovedSurroundingsRemoved;

    public static string Describe(this LeftoverDecision decision) => decision switch
    {
        LeftoverDecision.RemovedInsideOtherObject => "inside another mod's object",
        LeftoverDecision.RemovedSurroundingsRemoved => "surroundings removed",
        LeftoverDecision.KeptTooFewSurroundingObjects => "too few surrounding objects",
        LeftoverDecision.KeptSurroundingsMostlyKept => "surroundings mostly kept",
        LeftoverDecision.KeptProtectedType => "protected type",
        LeftoverDecision.KeptReferenced => "referenced",
        _ => throw new UnreachableException($"Unknown leftover decision {decision}."),
    };
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
    public bool IsRemoved => Decision.IsRemoval();

    /// <summary>The decision's reason, with the linking record for a referenced object.</summary>
    public string DescribeReason() =>
        KeepReason is { } keepReason ? $"{Decision.Describe()}: {keepReason.Detail}" : Decision.Describe();
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
