namespace AddedObjectRemover;

/// <summary>For each id of 0 to idCount - 1, the nodes whose list holds that id, kept in two flat arrays.</summary>
public sealed class InvertedLists
{
    private readonly int[] _offsets;
    private readonly int[] _nodes;

    private InvertedLists(int[] offsets, int[] nodes)
    {
        _offsets = offsets;
        _nodes = nodes;
    }

    /// <param name="idsOfNode">By node index: the ids that node lists. An id listed twice by one node counts twice.</param>
    public static InvertedLists Of(int idCount, IReadOnlyList<IReadOnlyList<int>> idsOfNode)
    {
        var offsets = new int[idCount + 1];
        foreach (var ids in idsOfNode)
            foreach (var id in ids) offsets[id + 1]++;
        for (var id = 0; id < idCount; id++) offsets[id + 1] += offsets[id];

        var nodes = new int[offsets[idCount]];
        var filled = new int[idCount];
        for (var node = 0; node < idsOfNode.Count; node++)
        {
            foreach (var id in idsOfNode[node]) nodes[offsets[id] + filled[id]++] = node;
        }
        return new InvertedLists(offsets, nodes);
    }

    /// <returns>The nodes listing the id, in ascending order.</returns>
    public ReadOnlySpan<int> NodesListing(int id) => _nodes.AsSpan(_offsets[id].._offsets[id + 1]);
}
