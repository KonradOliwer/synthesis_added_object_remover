namespace AddedObjectRemover;

public static class AdjacencyPairs
{
    /// <param name="neighborsOf">Parallel to <paramref name="nodes"/>: the neighbours of each node.</param>
    /// <param name="include">Decides, given a node and one of its neighbours, whether the pair counts.</param>
    /// <returns>
    /// The pairs from each node to every neighbour that <paramref name="include"/> accepts, in node order then neighbour
    /// order. A pair that is listed from both ends is made from both.
    /// </returns>
    public static List<TPair> Collect<TPair>(
        IReadOnlyList<int> nodes, IReadOnlyList<IReadOnlyList<int>> neighborsOf, Func<int, int, bool> include, Func<int, int, TPair> makePair)
    {
        var pairs = new List<TPair>();
        for (var i = 0; i < nodes.Count; i++)
        {
            foreach (var neighbor in neighborsOf[i])
            {
                if (include(nodes[i], neighbor)) pairs.Add(makePair(nodes[i], neighbor));
            }
        }
        return pairs;
    }

    /// <param name="include">Decides, given a node and a larger neighbour, whether the pair counts.</param>
    /// <returns>
    /// The pairs from each node to a larger neighbour that <paramref name="include"/> accepts, in node
    /// order then neighbour order. A pair that is listed from both ends is made once, from its smaller end.
    /// </returns>
    public static List<TPair> From<TPair>(
        IReadOnlyList<int> nodes, IReadOnlyList<IReadOnlyList<int>> neighborsOf, Func<int, int, bool> include, Func<int, int, TPair> makePair) =>
        Collect(nodes, neighborsOf, (node, neighbor) => neighbor > node && include(node, neighbor), makePair);
}
