using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The support rounds' evaluations joined with the removal decisions.</summary>
internal static class SupportEvaluationRows
{
    /// <param name="alsoRemove">A result of the support rounds.</param>
    /// <param name="decisions">The run's decisions with the support rounds applied.</param>
    /// <param name="iterationsBefore">The iterations of the passes before, which this pass's iterations are counted after.</param>
    /// <returns>Round by round, each in target order.</returns>
    public static ImmutableArray<SupportEvaluationRow> Join(RestingObjectsResult alsoRemove, IRemovalDecisions decisions, int iterationsBefore = 0) =>
    [
        .. alsoRemove.Rounds.SelectMany((round, index) => ((SupportRoundDetails)alsoRemove.Evidence[index]).Evaluations
            .Select(evaluation => Join(evaluation, round, iteration: iterationsBefore + index + 1, decisions))),
    ];

    private static SupportEvaluationRow Join(SupportEvaluation evaluation, Round round, int iteration, IRemovalDecisions decisions)
    {
        var decision = decisions.Of(new TargetId(evaluation.TargetIndex));
        var effect = RoundEffects.Of(decision, round);
        var removedAsLinked = !evaluation.Proposed && effect is { Outcome: RoundOutcome.Removed, AsLinked: true };
        return new SupportEvaluationRow(
            evaluation.TargetIndex,
            iteration,
            evaluation.Contacts,
            evaluation.Shares,
            Removed: evaluation.Proposed && effect.Outcome == RoundOutcome.Removed,
            RemovedAsLinked: removedAsLinked,
            RemovedAsLinkedToRestored: removedAsLinked && RoundEffects.FollowedARestoredMember(decision, decisions.Of),
            Restored: evaluation.Proposed && effect.Outcome == RoundOutcome.Restored,
            RestoredAsLinked: !evaluation.Proposed && effect is { Outcome: RoundOutcome.Restored, AsLinked: true },
            Held: evaluation.Proposed && effect.Outcome == RoundOutcome.KeptAsReferenced);
    }
}
