using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <remarks>The objects of any plugin are asked each time the rounds are created.</remarks>
internal sealed class AlsoRemoveRoundsAfter(RoundsAfterInput input) : IAlsoRemoveRoundsAfter
{
    public IRemovalRounds<RoundDetails> After(IRemovalDecisions decided, ImmutableArray<TargetObject> targets) =>
        RestingRounds.PrepareFrom(decided, RoundsInput.From(targets, input.Caches.VisibleObjectsOfAnyPlugin(), input.Caches, input.Execution), input.Settings).Rounds;
}
