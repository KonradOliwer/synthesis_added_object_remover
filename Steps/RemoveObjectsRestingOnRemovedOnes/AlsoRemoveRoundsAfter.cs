using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <remarks>The objects of any plugin are asked each time the rounds are created.</remarks>
internal sealed class AlsoRemoveRoundsAfter(RestingObjects.RoundsAfterInput input) : IAlsoRemoveRoundsAfter
{
    public IRemovalRounds<RoundDetails> After(IRemovalDecisions decided, ImmutableArray<TargetObject> targets) =>
        RestingRounds.PrepareFrom(decided, RoundsInput.From(targets, input.Caches.VisibleObjectsOfAnyPlugin(), input.Caches, input.Execution), input.Settings).Rounds;
}
