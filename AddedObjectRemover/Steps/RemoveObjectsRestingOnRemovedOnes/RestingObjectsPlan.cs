using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>What the also-remove step proposes to the runner: the rule whose rounds the runner applies.</summary>
/// <param name="Rounds">Null when no round runs: no seeds, or the mode is Nothing.</param>
/// <param name="Search">Null unless touch rounds run.</param>
internal sealed record RestingObjectsPlan(FollowUpRemovalMode Mode, bool HadSeeds, IRemovalRounds<RoundDetails>? Rounds, TouchSearch? Search);

/// <summary>The also-remove result with what the touch rounds built, which later steps reuse instead of building it again.</summary>
/// <param name="Search">Null unless touch rounds ran.</param>
internal sealed record RestingObjectsRun(RestingObjectsResult Result, TouchSearch? Search);
