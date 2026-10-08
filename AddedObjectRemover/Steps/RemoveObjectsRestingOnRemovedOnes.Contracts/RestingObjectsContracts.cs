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

/// <param name="HadSeeds">The too-close round removed at least one object.</param>
/// <param name="RemovalDecisions">The decisions with the also-remove rounds; the input decisions when none ran.</param>
/// <param name="Rounds">The also-remove rounds, in order.</param>
/// <param name="Evidence">One per also-remove round, in round order.</param>
public sealed record RestingObjectsResult(
    FollowUpRemovalMode Mode,
    bool HadSeeds,
    IRemovalDecisions RemovalDecisions,
    ImmutableArray<Round> Rounds,
    ImmutableArray<RoundDetails> Evidence,
    RestingObjectsWork Work)
{
    /// <summary>The objects the also-remove rounds kept.</summary>
    public int CountKept() => Rounds.Sum(round => RemovalDecisions.KeptIn(round).Length);
}

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
