using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The objects others rest on, as the fit step asks: those another target object rests on by the also-remove mode's relation
/// (<see cref="RestingRule"/>), and those a judged object of another mod rests on (<see cref="TargetsHoldingUpOtherModsObjects"/>)
/// whatever the also-remove mode and the setting for other mods' objects are, so no shrinking or turning leaves an object
/// without support.
/// </summary>
/// <param name="holding">Asked only when there is an object to check, since it asks for the objects of any plugin.</param>
internal sealed class AnyObjectRestingRule(
    ImmutableArray<TargetObject> targets, RestingRule targetObjects, Func<TargetsHoldingUpOtherModsObjects> holding) : IRestingRule
{
    public ImmutableArray<TargetId> FindObjectsOthersRestOn(IReadOnlyList<TargetId> objects)
    {
        if (objects.Count == 0) return [];

        var checkedObjects = objects.ToHashSet();
        var heldUp = holding().Find(checkedObjects.Select(target => targets[target.Index])).Keys.Where(checkedObjects.Contains);
        return [.. targetObjects.FindObjectsOthersRestOn(objects).Union(heldUp).OrderBy(target => target.Index)];
    }
}
