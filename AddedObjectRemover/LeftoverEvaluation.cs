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
internal sealed record LeftoverResult(IReadOnlyList<LeftoverEvaluation> Evaluations)
{
    public static LeftoverResult None { get; } = new([]);

    public int RemovedCount => Evaluations.Count(evaluation => evaluation.IsRemoved);

    /// <summary>A removal proposal for each object the rules would remove.</summary>
    public IReadOnlyList<Proposal> Proposals =>
    [
        .. Evaluations
            .Where(evaluation => evaluation.IsRemoved)
            .Select(evaluation => new Proposal(new TargetId(evaluation.TargetIndex), CauseOf(evaluation))),
    ];

    public int CountDecisions(LeftoverDecision decision) => Evaluations.Count(evaluation => evaluation.Decision == decision);

    /// <summary>The evaluations with the ledger's verdicts: an object the leftover round held is kept as referenced.</summary>
    public LeftoverResult WithVerdicts(Ledger ledger) => new(
    [
        .. Evaluations.Select(evaluation =>
            ledger.Of(new TargetId(evaluation.TargetIndex)) is Verdict.Held { Round.Kind: RoundKind.Leftover } held
                ? evaluation with { Decision = LeftoverDecision.KeptReferenced, KeepReason = held.Reason }
                : evaluation),
    ]);

    private static Cause CauseOf(LeftoverEvaluation evaluation) =>
        evaluation.ContainingObject is { } host ? new Cause.InsideRival(host.Id) : new Cause.SurroundingsCleared();
}
