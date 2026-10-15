using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>Work counts of the also-remove rounds.</summary>
/// <param name="Rounds">The also-remove rounds run.</param>
/// <param name="Candidates">ObjectsSupportedByIt only: distinct candidates evaluated at least once.</param>
/// <param name="Evaluations">ObjectsSupportedByIt only: candidate evaluations over all rounds.</param>
/// <param name="KeptWithoutContacts">ObjectsSupportedByIt only: candidates kept because no surface sample touched any supporter.</param>
/// <param name="Pairs">The rounds' pair tests.</param>
public sealed record RestingObjectsWork(int Rounds, int Candidates, int Evaluations, int KeptWithoutContacts, PairTestStats Pairs);

/// <summary>What the also-remove step did: its rounds, proposed to the run's decisions as one proposal, and what explains them.</summary>
/// <param name="Outcome">Switched off in mode Nothing; stopped by an unexpected error when the rounds failed, which then propose nothing.</param>
/// <param name="HadSeeds">The changed-terrain and too-close rounds together removed at least one object.</param>
/// <param name="SeedRounds">The rounds whose removals the also-remove rounds start from, in round order; empty when the step ran no rounds.</param>
/// <param name="Rounds">The also-remove rounds, in order.</param>
/// <param name="ProposedRounds">Each round's proposals, in round order, for the run's decisions to apply as rounds.</param>
/// <param name="Evidence">One per also-remove round, in round order.</param>
/// <param name="RoundsTime">The time of the rounds alone, without building the supporters' index and without the touch-chain search.</param>
/// <param name="TouchChains">Null unless touch rounds with seeds ran, the detailed log or the report files show them, and their search did not fail.</param>
/// <param name="Failure">The failure that stopped the rounds; null unless they were stopped.</param>
/// <param name="TouchChainsFailure">The failure that stopped the touch-chain search; null unless it was stopped.</param>
/// <param name="ReportFileDetails">
/// Computed after the rounds and the touch chains, through the same caches as the rounds, only when report files are written;
/// the problems the details met come with them.
/// </param>
public sealed record RestingObjectsResult(
    StepOutcome Outcome,
    FollowUpRemovalMode Mode,
    bool HadSeeds,
    ImmutableArray<Round> SeedRounds,
    ImmutableArray<Round> Rounds,
    ImmutableArray<IReadOnlyList<ProposedRemoval>> ProposedRounds,
    ImmutableArray<RoundDetails> Evidence,
    RestingObjectsWork Work,
    TimeSpan RoundsTime,
    TouchChainSet? TouchChains,
    PartFailure? Failure,
    PartFailure? TouchChainsFailure,
    ReportFileDetails ReportFileDetails);

/// <summary>The connected chains of the touch cascade.</summary>
/// <param name="ComponentOf">Target index -&gt; chain id, or -1 when not a node.</param>
/// <param name="ParentOf">Target index -&gt; the target index it was reached through, or -1 for a seed.</param>
/// <param name="DepthOf">Target index -&gt; its distance (edge count) from a seed.</param>
/// <param name="Members">Chain id -&gt; every target index in it: seeds, then round by round, each in target order.</param>
public sealed record TouchChainSet(
    TouchChainStatistics Stats,
    ImmutableArray<int> ComponentOf,
    ImmutableArray<int> ParentOf,
    ImmutableArray<int> DepthOf,
    ImmutableArray<ImmutableArray<int>> Members)
{
    public const int None = -1;
}
