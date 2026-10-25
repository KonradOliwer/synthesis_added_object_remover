using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The objects others rest on, as the fit step asks: those another target object rests on and would lose its support without,
/// unless the also-remove mode is Nothing (<see cref="RestingRule"/>), and those a judged object of another mod rests on (<see cref="TargetsHoldingUpOtherModsObjects"/>)
/// whatever the also-remove mode and the setting for other mods' objects are, so no shrinking or turning leaves an object
/// without support.
/// </summary>
/// <param name="heldUpAmong">The checked objects a judged object rests on; asked only when there is an object to check.</param>
internal sealed class AnyObjectRestingRule(RestingRule targetObjects, Func<IReadOnlySet<TargetId>, IEnumerable<TargetId>> heldUpAmong) : IRestingRule
{
    public ImmutableArray<TargetId> FindObjectsOthersRestOn(IReadOnlyList<TargetId> objects)
    {
        if (objects.Count == 0) return [];

        return [.. targetObjects.FindObjectsOthersRestOn(objects).Union(heldUpAmong(objects.ToHashSet())).OrderBy(target => target.Index)];
    }
}
