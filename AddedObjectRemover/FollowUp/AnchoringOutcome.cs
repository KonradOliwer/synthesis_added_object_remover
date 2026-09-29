using System.Collections.Immutable;

namespace AddedObjectRemover;

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

/// <param name="Iterations">The follow-up rounds.</param>
/// <param name="Candidates">Distinct candidates evaluated at least once.</param>
/// <param name="KeptWithoutContacts">Candidates kept because no surface sample touched any supporter.</param>
internal sealed record AnchoringStats(
    int Iterations,
    int Candidates,
    int Evaluations,
    int KeptWithoutContacts,
    PairTestStats Pairs,
    TimeSpan Setup,
    TimeSpan TouchSearch,
    TimeSpan ContactPoints);

/// <param name="Removals">The follow-up rounds' removals: lost support and the linked group members removed with them.</param>
/// <param name="Kept">The objects the follow-up rounds held.</param>
/// <param name="Evaluations">Round by round, each in target order.</param>
internal sealed record AnchoringResult(
    List<Removal> Removals,
    List<KeptTarget> Kept,
    AnchoringStats Stats,
    List<AnchoringEvaluation> Evaluations);

/// <summary>The support cascade's report records: its rounds' evaluations joined with the ledger's verdicts.</summary>
internal static class AnchoringOutcome
{
    /// <param name="rounds">The follow-up rounds, in order.</param>
    /// <param name="evidence">The support rounds' evidence, one per follow-up round.</param>
    public static AnchoringResult Create(
        Ledger ledger,
        LedgerReport report,
        ImmutableArray<Round> rounds,
        ImmutableArray<RoundEvidence> evidence,
        SupportRule rule,
        TimeSpan setup)
    {
        var evaluations = rounds
            .SelectMany((round, index) => ((SupportRound)evidence[index]).Evaluations.Select(evaluation => Join(evaluation, round, iteration: index + 1, ledger)))
            .ToList();
        var stats = new AnchoringStats(
            rounds.Length,
            rule.CandidatesWithContacts,
            evaluations.Count,
            evaluations.Where(evaluation => evaluation.Contacts.ContactPoints == 0).Select(evaluation => evaluation.TargetIndex).Distinct().Count(),
            rule.PairStats,
            setup,
            rule.TouchSearch,
            rule.ContactPoints);
        return new AnchoringResult(
            [.. rounds.SelectMany(report.RemovalsIn)],
            [.. rounds.SelectMany(round => LedgerReport.KeptIn(ledger, round))],
            stats,
            evaluations);
    }

    /// <param name="iteration">The round's position among the follow-up rounds, from 1.</param>
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
