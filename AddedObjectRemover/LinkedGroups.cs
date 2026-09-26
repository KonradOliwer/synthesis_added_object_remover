using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>A form link from one target-plugin placed record, a checked target object, to another target-plugin FormKey.</summary>
internal readonly record struct TargetLink(FormKey Source, FormKey Linked);

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

    /// <param name="links">Links whose ends are not both target objects are ignored.</param>
    public static LinkedGroups Build(IReadOnlyList<TargetObject> targets, IEnumerable<TargetLink> links)
    {
        var indexByFormKey = Enumerable.Range(0, targets.Count).ToDictionary(index => targets[index].Record.FormKey);
        var roots = Enumerable.Range(0, targets.Count).ToArray();
        foreach (var link in links)
        {
            if (indexByFormKey.TryGetValue(link.Source, out var source) && indexByFormKey.TryGetValue(link.Linked, out var linked))
            {
                Join(roots, source, linked);
            }
        }
        return CreateGroups(roots);
    }

    /// <summary>Every group, single objects included.</summary>
    public IReadOnlyList<IReadOnlyList<int>> All => _groups;

    /// <summary>The target's group, the target included.</summary>
    public IReadOnlyList<int> MembersOf(int targetIndex) => _groups[_groupOf[targetIndex]];

    public bool IsLinked(int targetIndex) => MembersOf(targetIndex).Count > 1;

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
