using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>
/// The also-remove rounds seeded by given rounds, as the also-remove mode says, until a round removes nothing, decided on a working
/// copy of the earlier decisions, which never change; then the touch chains when they are shown, then the report-file details when
/// report files are written, each stopped on its own by a user-data failure.
/// </summary>
public interface IRestingObjectsRounds
{
    /// <param name="targets">Where the rounds measure them: an object kept shrunk or turned at its new placement, a restored one where the cleaned mod put it.</param>
    /// <param name="earlierRounds">The decisions the rounds start from; the objects they kept never take part in the touch rounds.</param>
    /// <param name="seedRounds">The rounds of <paramref name="earlierRounds"/> whose removals the rounds start from, in round order.</param>
    RestingObjectsResult Find(ImmutableArray<TargetObject> targets, IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds);
}
