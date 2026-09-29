using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Target objects connected, in either direction, by links between them (Enable Parent, Linked
/// References, Activate Parents, Attach Ref, ...). A group is removed whole or kept whole.
/// Groups and their members are ordered by target index, so everything built on them is deterministic.
/// </summary>
internal sealed class LinkedGroups
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
        var roots = Enumerable.Range(0, targetCount).ToArray();
        foreach (var link in links) Join(roots, link.From.Index, link.To.Index);
        return CreateGroups(roots);
    }

    /// <summary>Every group, single objects included.</summary>
    public IReadOnlyList<IReadOnlyList<int>> All => _groups;

    /// <summary>The target's group, the target included.</summary>
    public IReadOnlyList<int> MembersOf(int targetIndex) => _groups[_groupOf[targetIndex]];

    public bool IsLinked(int targetIndex) => MembersOf(targetIndex).Count > 1;

    /// <summary>The groups with more than one member.</summary>
    public IEnumerable<IReadOnlyList<int>> MultiMemberGroups => _groups.Where(members => members.Length > 1);

    /// <summary>
    /// The spaces of <paramref name="seeds"/> and every space removals can spread to from there
    /// through linked groups: once a space is collected, so are the spaces of every member of each
    /// group with a member in it.
    /// </summary>
    public HashSet<FormKey> CollectReachableSpaces(IReadOnlyList<TargetObject> targets, IEnumerable<int> seeds)
    {
        var spaces = seeds.Select(seed => targets[seed].SpaceKey).ToHashSet();
        var groupSpaces = MultiMemberGroups
            .Select(members => members.Select(member => targets[member].SpaceKey).ToHashSet())
            .Where(memberSpaces => memberSpaces.Count > 1)
            .ToList();
        bool added;
        do
        {
            added = false;
            foreach (var memberSpaces in groupSpaces.Where(spaces.Overlaps))
            {
                foreach (var space in memberSpaces) added |= spaces.Add(space);
            }
        }
        while (added);
        return spaces;
    }

    /// <param name="decided">The removals one step decided, in its order.</param>
    /// <param name="removed">Every target index removed so far, <paramref name="decided"/> included.</param>
    /// <returns>The other members of the decided removals' groups not removed yet.</returns>
    public List<LinkedRemoval> CollectLinkedRemovals(IEnumerable<Removal> decided, IReadOnlySet<int> removed)
    {
        var added = new HashSet<int>();
        var linked = new List<LinkedRemoval>();
        foreach (var removal in decided)
        {
            foreach (var member in MembersOf(removal.TargetIndex))
            {
                if (!removed.Contains(member) && added.Add(member)) linked.Add(new LinkedRemoval(member, removal.TargetIndex));
            }
        }
        return linked;
    }

    private static void Join(int[] roots, int first, int second)
    {
        var firstRoot = FindRoot(roots, first);
        var secondRoot = FindRoot(roots, second);
        if (firstRoot == secondRoot) return;
        roots[Math.Max(firstRoot, secondRoot)] = Math.Min(firstRoot, secondRoot);
    }

    /// <remarks>Halves the path on the way, so chains stay short.</remarks>
    private static int FindRoot(int[] roots, int node)
    {
        while (roots[node] != node)
        {
            roots[node] = roots[roots[node]];
            node = roots[node];
        }
        return node;
    }

    private static LinkedGroups CreateGroups(int[] roots)
    {
        var groupOf = new int[roots.Length];
        var groups = new List<List<int>>();
        var groupByRoot = new Dictionary<int, int>();
        for (var index = 0; index < roots.Length; index++)
        {
            var root = FindRoot(roots, index);
            if (!groupByRoot.TryGetValue(root, out var group))
            {
                group = groups.Count;
                groupByRoot[root] = group;
                groups.Add([]);
            }
            groups[group].Add(index);
            groupOf[index] = group;
        }
        return new LinkedGroups(groups.Select(members => members.ToArray()).ToArray(), groupOf);
    }
}
