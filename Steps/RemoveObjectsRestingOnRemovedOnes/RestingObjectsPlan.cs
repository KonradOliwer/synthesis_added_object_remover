using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>The rule whose rounds the step decides on its own copy of the decisions.</summary>
/// <param name="Rounds">Null when no round runs: no seeds, or the mode is Nothing.</param>
/// <param name="Search">Null unless touch rounds run.</param>
internal sealed record RestingObjectsPlan(FollowUpRemovalMode Mode, bool HadSeeds, IRemovalRounds<RoundDetails>? Rounds, TouchSearch? Search);

/// <summary>The also-remove result with what the rounds built, which the touch chains and the report-file details reuse instead of building it again.</summary>
/// <param name="Decisions">The step's own copy of the decisions with its rounds; the decisions it started from when none ran.</param>
/// <param name="Search">Null unless touch rounds ran.</param>
internal sealed record RestingObjectsRun(RestingObjectsResult Result, IRemovalDecisions Decisions, TouchSearch? Search);
