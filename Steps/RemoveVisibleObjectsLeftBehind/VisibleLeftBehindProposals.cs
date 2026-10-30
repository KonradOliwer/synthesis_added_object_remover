using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

internal static class VisibleLeftBehindProposals
{
    /// <summary>A removal proposal for each object the rules would remove.</summary>
    public static IReadOnlyList<ProposedRemoval> ProposalsOf(IEnumerable<VisibleLeftBehindCheck> evaluations) =>
    [
        .. evaluations
            .Where(evaluation => evaluation.IsRemoved)
            .Select(evaluation => new ProposedRemoval(new TargetId(evaluation.TargetIndex), new RemovalReason.VisibleObjectSurroundingsRemoved())),
    ];
}
