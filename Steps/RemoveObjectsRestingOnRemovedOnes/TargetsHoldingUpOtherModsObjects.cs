using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The target objects that judged objects of other mods rest on, each where it stands before any decision: the target objects
/// the object loses its support without, by the test that decides which objects are left without support
/// (<see cref="SupportLoss.FindTargetsHoldingUp"/>). Thread-safe; results depend only on the inputs.
/// </summary>
/// <param name="world">Every target object and object of any plugin where it stands before any decision.</param>
internal sealed class TargetsHoldingUpOtherModsObjects(
    SupportWorld world,
    JudgedObjectsNear near,
    OtherModsObjectRules rules,
    SupportLoss loss,
    Execution execution)
{
    /// <param name="nearTargets">The judged objects whose box comes within the gap of one of theirs are checked.</param>
    /// <returns>Each target object that holds up a checked object, with the first such object in id order.</returns>
    public Dictionary<TargetId, OtherObject> Find(IEnumerable<TargetObject> nearTargets)
    {
        var resting = near.FindTouchingAny([.. nearTargets.Select(rules.SupportingBoxOf).OfType<BoxInSpace>()], isLeftOut: _ => false);
        var holders = ParallelMap.Run(execution, resting.Count, () => new OtherObjectQueries(), (k, queries) => FindTargetsHoldingUp(resting[k], queries), ParallelMap.AutomaticRangeSize);
        var found = new Dictionary<TargetId, OtherObject>();
        foreach (var (other, holdersOfOther) in resting.Zip(holders))
        {
            foreach (var target in holdersOfOther) found.TryAdd(target, other);
        }
        return found;
    }

    /// <summary>The target objects holding up a target object, before any decision.</summary>
    /// <param name="target">A visible target object with a mesh.</param>
    public List<TargetId> FindHolding(TargetObject target, OtherObjectQueries queries) => loss.FindTargetsHoldingUp(rules.StandingOf(target), world, queries);

    private List<TargetId> FindTargetsHoldingUp(OtherObject other, OtherObjectQueries queries) => loss.FindTargetsHoldingUp(rules.StandingOf(other), world, queries);
}
