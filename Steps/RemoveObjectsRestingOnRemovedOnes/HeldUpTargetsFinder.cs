using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// KeepSupportingObject: the target objects that hold up other mods' objects (<see cref="TargetsHoldingUpOtherModsObjects"/>),
/// each where it stands before any decision. Round by round, the target objects that hold up a target object found so are added
/// too, by the same test, so the whole support under another mod's object stays. Each target object keeps the first object found
/// resting on it, in id order, so the result does not depend on the thread count.
/// </summary>
internal sealed class HeldUpTargetsFinder(ImmutableArray<TargetObject> targets, TargetsHoldingUpOtherModsObjects holding, Execution execution)
{
    public Dictionary<TargetId, RestingObject> Find()
    {
        var restingOn = holding.Find(targets).ToDictionary(entry => entry.Key, entry => new RestingObject(entry.Value.Key, entry.Value.EditorId, KeptFor: null));
        AddTargetsBelow(restingOn);
        return restingOn;
    }

    /// <summary>Adds, round by round, the target objects that hold up a target object added in the round before.</summary>
    private void AddTargetsBelow(Dictionary<TargetId, RestingObject> restingOn)
    {
        List<TargetId> frontier = [.. restingOn.Keys.Order()];
        while (frontier.Count > 0)
        {
            var holdersOf = ParallelMap.Run(
                execution, frontier.Count, () => new OtherObjectQueries(), (k, queries) => holding.FindHolding(targets[frontier[k].Index], queries), ParallelMap.AutomaticRangeSize);
            var added = new List<TargetId>();
            for (var k = 0; k < frontier.Count; k++)
            {
                var held = targets[frontier[k].Index];
                foreach (var target in holdersOf[k])
                {
                    if (restingOn.TryAdd(target, new RestingObject(held.Key, held.EditorId, restingOn[held.Id]))) added.Add(target);
                }
            }
            frontier = [.. added.Order()];
        }
    }
}
