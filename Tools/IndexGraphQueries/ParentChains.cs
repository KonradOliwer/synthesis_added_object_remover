namespace AddedObjectRemover;

public static class ParentChains
{
    /// <param name="parentOf">Index to its parent index; a negative value marks a root.</param>
    /// <returns>The nodes from the root down to <paramref name="node"/>, the node last.</returns>
    public static List<int> ToRoot(int node, IReadOnlyList<int> parentOf)
    {
        var chain = new List<int>();
        for (var current = node; current >= 0; current = parentOf[current]) chain.Add(current);
        chain.Reverse();
        return chain;
    }
}
