using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

/// <summary>The also-remove rounds that spread, as the also-remove mode says, from removals the step made on its working copy.</summary>
public interface IAlsoRemoveRoundsAfter
{
    /// <param name="decided">The decisions the seeds were removed in; the objects they kept never take part in the touch rounds.</param>
    /// <param name="targets">Where the rounds measure them.</param>
    IRemovalRounds<RoundDetails> After(IRemovalDecisions decided, ImmutableArray<TargetObject> targets);
}
