using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The target objects that judged objects of other mods rest on, each where it stands before any decision. Target objects hold
/// up another object when taking them away would leave it without at least the threshold share of its support, the relation
/// <see cref="RestingRule"/> uses between target objects: each target object holding that share alone, or, when none does, every
/// target object holding part of it when they hold that share together. The other object must also rest on what is below it and
/// no longer rest without those target objects, so a target object standing on it or beside it does not hold it up.
/// Thread-safe; results depend only on the inputs.
/// </summary>
/// <param name="world">Every target object and object of any plugin where it stands before any decision.</param>
internal sealed class TargetsHoldingUpOtherModsObjects(
    SupportWorld world,
    JudgedObjectsNear near,
    OtherModsObjectRules rules,
    OtherObjectSupport support,
    DropFinder drops,
    float threshold,
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

    /// <summary>The target objects that hold up an object with these contacts by the share of its support they hold.</summary>
    /// <returns>
    /// Each target object holding at least the threshold share alone; when none does, all target objects holding part of it, when
    /// together they hold at least the threshold share; otherwise none.
    /// </returns>
    public static List<TargetId> FindTargetsHoldingEnough(CandidateContacts contacts, float threshold)
    {
        List<TargetId> alone = [.. SupportRule.FindSupportersHoldingEnough(contacts, threshold).Where(IsTarget).Select(ToTargetId)];
        if (alone.Count > 0) return alone;

        List<SupporterWeight> together = [.. contacts.Supporters.Where(entry => IsTarget(entry.Supporter))];
        return together.Count > 0 && SupportRule.ReachesThreshold(together.Sum(entry => entry.Weight) / contacts.TotalWeight, threshold)
            ? [.. together.Select(entry => ToTargetId(entry.Supporter))]
            : [];
    }

    /// <returns>
    /// None for an object that does not rest on what is below it, which only touches things beside it, and for one that would
    /// still rest on what is below it without them: those target objects only stand on it or beside it.
    /// </returns>
    private List<TargetId> FindTargetsHoldingUp(OtherObject other, OtherObjectQueries queries)
    {
        var holders = FindTargetsHoldingEnough(support.FindContacts(other, world, queries), threshold);
        return holders.Count > 0 && drops.Rests(other, world, queries) && !drops.Rests(other, world.Without(holders), queries) ? holders : [];
    }

    private static bool IsTarget(Supporter supporter) => supporter.Type == SupporterType.Target;

    private static TargetId ToTargetId(Supporter supporter) => new(supporter.Index);
}
