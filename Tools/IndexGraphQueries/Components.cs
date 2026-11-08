namespace AddedObjectRemover;

/// <summary>The connected components of a graph over the indices 0 to count - 1.</summary>
public static class Components
{
    /// <returns>
    /// Every component, single nodes included. Components are numbered by their lowest member and
    /// list their members in ascending order, whatever the order of the edges.
    /// </returns>
    public static List<int[]> Of(int count, IEnumerable<(int First, int Second)> edges)
    {
        var roots = new UnionFind(count);
        foreach (var (first, second) in edges) roots.Join(first, second);

        var members = new List<List<int>>();
        var componentOfRoot = new Dictionary<int, int>();
        for (var node = 0; node < count; node++)
        {
            var root = roots.Find(node);
            if (!componentOfRoot.TryGetValue(root, out var component))
            {
                component = members.Count;
                componentOfRoot[root] = component;
                members.Add([]);
            }
            members[component].Add(node);
        }
        return [.. members.Select(component => component.ToArray())];
    }

    /// <param name="components">Components of the indices 0 to count - 1 that cover each index once, as <see cref="Of"/> gives them.</param>
    /// <returns>For each index, the position of its component in <paramref name="components"/>.</returns>
    public static int[] ComponentOfEachNode(int count, IReadOnlyList<int[]> components)
    {
        var componentOf = new int[count];
        for (var component = 0; component < components.Count; component++)
        {
            foreach (var node in components[component]) componentOf[node] = component;
        }
        return componentOf;
    }
}
