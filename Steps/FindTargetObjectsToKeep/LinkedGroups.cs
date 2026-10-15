using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <summary>
/// Target objects connected, in either direction, by links between them (Enable Parent, Linked
/// References, Activate Parents, Attach Ref, ...). A group is removed whole or kept whole.
/// Groups and their members are ordered by target index, so everything built on them is deterministic.
/// </summary>
internal sealed class LinkedGroups : ILinkedGroups
{
    private readonly int[][] _groups;
    private readonly int[] _groupOf;

    private LinkedGroups(int[][] groups, int[] groupOf)
    {
        _groups = groups;
        _groupOf = groupOf;
    }

    public static LinkedGroups Build(int targetCount, IEnumerable<TargetLink> links)
    {
        var groups = Components.Of(targetCount, links.Select(link => (link.From.Index, link.To.Index))).ToArray();
        var groupOf = new int[targetCount];
        for (var group = 0; group < groups.Length; group++)
        {
            foreach (var member in groups[group]) groupOf[member] = group;
        }
        return new LinkedGroups(groups, groupOf);
    }

    /// <summary>Every group, single objects included.</summary>
    public IReadOnlyList<IReadOnlyList<int>> All => _groups;

    /// <summary>The target's group, the target included.</summary>
    public IReadOnlyList<int> MembersOf(int targetIndex) => _groups[_groupOf[targetIndex]];

    public bool IsLinked(int targetIndex) => MembersOf(targetIndex).Count > 1;

    /// <summary>The groups with more than one member.</summary>
    public IEnumerable<IReadOnlyList<int>> MultiMemberGroups => _groups.Where(members => members.Length > 1);
}
