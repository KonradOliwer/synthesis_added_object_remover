using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

internal static class LeftBehindRound
{
    /// <summary>A removal proposal for each object the rules would remove.</summary>
    public static IReadOnlyList<ProposedRemoval> Proposals(LeftBehindResult result) =>
    [
        .. result.Evaluations
            .Where(evaluation => evaluation.IsRemoved)
            .Select(evaluation => new ProposedRemoval(new TargetId(evaluation.TargetIndex), ReasonOf(evaluation))),
    ];

    /// <summary>The evaluations with the decisions: an object the left-behind round kept is kept as referenced.</summary>
    public static LeftBehindResult WithVerdicts(LeftBehindResult result, IRemovalDecisions decisions) => new(
    [
        .. result.Evaluations.Select(evaluation =>
            decisions.Of(new TargetId(evaluation.TargetIndex)) is Decision.Kept { Round.Kind: RoundKind.LeftBehind } kept
                ? evaluation with { Decision = LeftBehindOutcome.KeptReferenced, KeepReason = kept.KeepReason }
                : evaluation),
    ]);

    private static RemovalReason ReasonOf(LeftBehindCheck evaluation) =>
        evaluation.ContainingObject is { } container
            ? new RemovalReason.InsideAnotherModsObject(container.Id)
            : new RemovalReason.SurroundingsRemoved();
}
