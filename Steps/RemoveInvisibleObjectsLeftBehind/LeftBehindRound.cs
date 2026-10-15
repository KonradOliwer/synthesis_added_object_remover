using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

internal static class LeftBehindRound
{
    /// <summary>A removal proposal for each object the rules would remove.</summary>
    public static IReadOnlyList<ProposedRemoval> ProposalsOf(IEnumerable<LeftBehindCheck> evaluations) =>
    [
        .. evaluations
            .Where(evaluation => evaluation.IsRemoved)
            .Select(evaluation => new ProposedRemoval(new TargetId(evaluation.TargetIndex), ReasonOf(evaluation))),
    ];

    private static RemovalReason ReasonOf(LeftBehindCheck evaluation) =>
        evaluation.ContainingObject is { } container
            ? new RemovalReason.InsideAnotherModsObject(container.Id)
            : new RemovalReason.SurroundingsRemoved();
}
