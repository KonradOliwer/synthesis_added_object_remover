using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

internal static class ReachableSpaces
{
    /// <summary>
    /// The spaces of <paramref name="seeds"/> and every space removals can spread to from there
    /// through linked groups: once a space is collected, so are the spaces of every member of each
    /// group with a member in it.
    /// </summary>
    public static HashSet<RecordKey> CollectReachableSpaces(ILinkedGroups groups, IReadOnlyList<TargetObject> targets, IEnumerable<int> seeds)
    {
        var groupSpaces = groups.MultiMemberGroups
            .Select(members => members.Select(member => targets[member].SpaceKey).ToHashSet())
            .Where(memberSpaces => memberSpaces.Count > 1)
            .ToList();
        return SetClosure.Of(seeds.Select(seed => targets[seed].SpaceKey), groupSpaces, EqualityComparer<RecordKey>.Default);
    }
}
