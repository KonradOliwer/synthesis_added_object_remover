namespace AddedObjectRemover;

/// <summary>Disjoint sets of the indices 0 to count - 1. The smaller root wins a join, so the roots do not depend on the join order.</summary>
internal sealed class UnionFind(int count)
{
    private readonly int[] _roots = Enumerable.Range(0, count).ToArray();

    public int Count => _roots.Length;

    public void Join(int first, int second)
    {
        var firstRoot = Find(first);
        var secondRoot = Find(second);
        if (firstRoot != secondRoot) _roots[Math.Max(firstRoot, secondRoot)] = Math.Min(firstRoot, secondRoot);
    }

    /// <remarks>Halves the path on the way, so chains stay short.</remarks>
    public int Find(int node)
    {
        while (_roots[node] != node)
        {
            _roots[node] = _roots[_roots[node]];
            node = _roots[node];
        }
        return node;
    }
}
