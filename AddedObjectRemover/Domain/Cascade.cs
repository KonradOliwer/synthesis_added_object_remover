using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>A follow-up removal rule, applied round by round from the objects the previous round removed.</summary>
internal interface IFollowUpRule
{
    /// <param name="removedLastRound">The previous round's removals, linked ones included.</param>
    RoundProposals Next(Ledger ledger, ImmutableArray<TargetId> removedLastRound);
}

/// <summary>A rule's proposals for one round and what it found on the way.</summary>
internal sealed record RoundProposals(IReadOnlyList<Proposal> Proposals, RoundEvidence Evidence);

internal abstract record RoundEvidence;

/// <param name="Reached">Each object reached by touch, paired with the frontier object that touched it first.</param>
/// <param name="Work">The round's pair tests.</param>
internal sealed record TouchRound(ImmutableArray<TargetPair> Reached, PairTestStats Work) : RoundEvidence;

/// <summary>Runs a follow-up rule in FollowUp rounds until a round removes nothing.</summary>
internal static class Cascade
{
    /// <returns>The ledger with the follow-up rounds, and each round's evidence in round order.</returns>
    public static (Ledger Ledger, ImmutableArray<RoundEvidence> Rounds) Run(Ledger ledger, IFollowUpRule rule)
    {
        var rounds = ImmutableArray.CreateBuilder<RoundEvidence>();
        var frontier = ledger.RemovedIn(ledger.Rounds[^1]);
        while (!frontier.IsEmpty)
        {
            var next = rule.Next(ledger, frontier);
            rounds.Add(next.Evidence);
            ledger = ledger.Apply(RoundKind.FollowUp, next.Proposals);
            frontier = ledger.RemovedIn(ledger.Rounds[^1]);
        }
        return (ledger, rounds.ToImmutable());
    }
}
