using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The support rounds' evaluations joined with the removal decisions.</summary>
internal static class AnchoringRows
{
    /// <param name="alsoRemove">A result of the support rounds.</param>
    /// <returns>Round by round, each in target order.</returns>
    public static ImmutableArray<AnchoringEvaluation> Join(RestingObjectsResult alsoRemove) =>
    [
        .. alsoRemove.Rounds.SelectMany((round, index) => ((SupportRoundDetails)alsoRemove.Evidence[index]).Evaluations
            .Select(evaluation => Join(evaluation, round, iteration: index + 1, alsoRemove.RemovalDecisions))),
    ];

    private static AnchoringEvaluation Join(SupportEvaluation evaluation, Round round, int iteration, IRemovalDecisions decisions)
    {
        var decision = decisions.Of(new TargetId(evaluation.TargetIndex));
        var decidedThisRound = decision?.Round == round;
        return new AnchoringEvaluation(
            evaluation.TargetIndex,
            iteration,
            evaluation.Contacts,
            evaluation.Shares,
            Removed: evaluation.Proposed && decidedThisRound && decision is Decision.Removed,
            RemovedAsLinked: !evaluation.Proposed && decidedThisRound && decision is Decision.Removed { Reason: RemovalReason.LinkedTo },
            Held: evaluation.Proposed && decidedThisRound && decision is Decision.Kept);
    }
}
