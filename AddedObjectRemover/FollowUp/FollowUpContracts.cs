using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <param name="Solids">Only for ObjectsSupportedByIt: every visible object of any plugin that can support a target.</param>
/// <param name="Terrain">Empty unless ObjectsSupportedByIt or relocation is on.</param>
/// <param name="Timer">Times the sub-phases the detailed log reports.</param>
internal sealed record FollowUpInput(
    ImmutableArray<TargetObject> Targets,
    TargetLooks Looks,
    Protection Protection,
    ISolids? Solids,
    ShapeCatalog Shapes,
    TriangleStore Triangles,
    TerrainHeights Terrain,
    Execution Exec,
    IPhaseTimer Timer);

/// <summary>Work counts of the follow-up rounds.</summary>
/// <param name="Rounds">The follow-up rounds run.</param>
/// <param name="Candidates">ObjectsSupportedByIt only: distinct candidates evaluated at least once.</param>
/// <param name="Evaluations">ObjectsSupportedByIt only: candidate evaluations over all rounds.</param>
/// <param name="KeptWithoutContacts">ObjectsSupportedByIt only: candidates kept because no surface sample touched any supporter.</param>
/// <param name="Pairs">The rounds' pair tests.</param>
internal sealed record FollowUpWork(int Rounds, int Candidates, int Evaluations, int KeptWithoutContacts, PairTestStats Pairs);

/// <summary>What the follow-up rounds built and later explanations reuse, so they need not build it again.</summary>
internal sealed record FollowUpContext(TouchSearch Search);

/// <param name="HadSeeds">The too-close round removed at least one object.</param>
/// <param name="Ledger">The ledger with the follow-up rounds; the input ledger when none ran.</param>
/// <param name="Rounds">The follow-up rounds, in order.</param>
/// <param name="Evidence">One per follow-up round, in round order.</param>
/// <param name="Context">Null unless touch rounds ran.</param>
internal sealed record FollowUpResult(
    FollowUpRemovalMode Mode,
    bool HadSeeds,
    Ledger Ledger,
    ImmutableArray<Round> Rounds,
    ImmutableArray<RoundEvidence> Evidence,
    FollowUpWork Work,
    FollowUpContext? Context)
{
    /// <summary>The follow-up rounds' removals by touch or lost support, without the linked group members removed with them.</summary>
    public int CountRemovedByRule() =>
        Rounds.Sum(round => Ledger.RemovedIn(round).Count(target => Ledger.Of(target)!.Cause is not Cause.Linked));

    /// <summary>The objects the follow-up rounds held.</summary>
    public int CountHeld() => Rounds.Sum(round => Ledger.HeldIn(round).Length);
}

/// <summary>The connected components of the touch cascade.</summary>
/// <param name="ComponentOf">Target index -&gt; component id, or -1 when not a node.</param>
/// <param name="ParentOf">Target index -&gt; the target index it was reached through, or -1 for a seed.</param>
/// <param name="DepthOf">Target index -&gt; its distance (edge count) from a seed.</param>
/// <param name="Members">Component id -&gt; every target index in it: seeds, then round by round, each in target order.</param>
internal sealed record TouchComponentSet(
    TouchStats Stats,
    ImmutableArray<int> ComponentOf,
    ImmutableArray<int> ParentOf,
    ImmutableArray<int> DepthOf,
    ImmutableArray<ImmutableArray<int>> Members)
{
    public const int None = -1;
}
