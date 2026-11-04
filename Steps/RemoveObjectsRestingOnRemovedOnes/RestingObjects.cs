using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove step: seeded with the removals of the rounds before it, its rounds remove what goes with them, as the
/// also-remove mode says, until a round removes nothing. It decides the rounds on its own copy of the decisions, then adds
/// them to the run's decisions.
/// </summary>
public static class RestingObjects
{
    /// <param name="Targets">Where they stood before any decision.</param>
    /// <param name="Decisions">The decisions with the rounds of the steps before, whose removals the rounds follow up; the step's rounds are added to them.</param>
    /// <param name="Rounds">The also-remove rounds the step runs.</param>
    public sealed record Input(ImmutableArray<TargetObject> Targets, IRunDecisions Decisions, IRestingObjectsRounds Rounds);

    /// <remarks>Seeded with the removals of every round so far, measuring the target objects where those rounds measure them.</remarks>
    public static Decided<RestingObjectsResult> Find(Input input)
    {
        var earlierRounds = input.Decisions;
        var result = input.Rounds.Find(earlierRounds.TargetsAsTheRoundsMeasureThem(input.Targets), earlierRounds, earlierRounds.Rounds);
        return new Decided<RestingObjectsResult>(result, earlierRounds.ApplyRounds(AlsoRemoveRounds.Opening, result.ProposedRounds));
    }
}
