using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <param name="Iteration">The follow-up round that evaluated it, from 1.</param>
/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
/// <param name="Removed">Removed for its own lost support.</param>
/// <param name="RemovedAsLinked">Not removed for its own support, but in the same round with a linked object that was.</param>
/// <param name="Held">It would have lost enough support, but it is protected.</param>
internal sealed record AnchoringEvaluation(
    int TargetIndex,
    int Iteration,
    CandidateContacts Contacts,
    IReadOnlyList<SupporterShare> Shares,
    bool Removed,
    bool RemovedAsLinked,
    bool Held)
{
    public float RemovedShare => ShareOf(SupportCategory.RemovedTarget);

    public float ShareOf(SupportCategory category) =>
        Shares.Where(share => share.Category == category).Sum(share => share.Share);
}

/// <summary>The support rounds' evaluations joined with the ledger's verdicts.</summary>
internal static class AnchoringRows
{
    /// <param name="followUp">A result of the support rounds.</param>
    /// <returns>Round by round, each in target order.</returns>
    public static ImmutableArray<AnchoringEvaluation> Join(FollowUpResult followUp) =>
    [
        .. followUp.Rounds.SelectMany((round, index) => ((SupportRound)followUp.Evidence[index]).Evaluations
            .Select(evaluation => Join(evaluation, round, iteration: index + 1, followUp.Ledger))),
    ];

    private static AnchoringEvaluation Join(SupportEvaluation evaluation, Round round, int iteration, Ledger ledger)
    {
        var verdict = ledger.Of(new TargetId(evaluation.TargetIndex));
        var decidedThisRound = verdict?.Round == round;
        return new AnchoringEvaluation(
            evaluation.TargetIndex,
            iteration,
            evaluation.Contacts,
            evaluation.Shares,
            Removed: evaluation.Proposed && decidedThisRound && verdict is Verdict.Removed,
            RemovedAsLinked: !evaluation.Proposed && decidedThisRound && verdict is Verdict.Removed { Cause: Cause.Linked },
            Held: evaluation.Proposed && decidedThisRound && verdict is Verdict.Held);
    }
}
