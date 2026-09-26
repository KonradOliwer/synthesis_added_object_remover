namespace AddedObjectRemover;

/// <summary>
/// Decides which candidate pairs the cluster search can ever ask about, without any mesh work.
/// The search only tests pairs with one end in its frontier, and a frontier node is a seed or a
/// target that is removed (not kept), reached from a seed through touching pairs. Touching pairs
/// are candidate pairs, so every frontier node lies in a seed's connected component of the
/// candidate graph restricted to such "propagating" nodes. A pair is needed only if one of its
/// propagating ends lies in such a component; pairs between two kept targets are never needed.
/// </summary>
internal static class SeedReachability
{
    public static bool[] FindPairsToTest(TouchCandidates candidates, bool[] propagates, IEnumerable<int> seeds)
    {
        var components = new DisjointSets(propagates.Length);
        foreach (var pair in candidates.Pairs)
        {
            if (propagates[pair.First] && propagates[pair.Second]) components.Union(pair.First, pair.Second);
        }

        var seedRoots = seeds.Select(components.Find).ToHashSet();
        bool ReachesSeed(int node) => propagates[node] && seedRoots.Contains(components.Find(node));

        var needed = new bool[candidates.Pairs.Count];
        for (var k = 0; k < needed.Length; k++)
        {
            var pair = candidates.Pairs[k];
            needed[k] = ReachesSeed(pair.First) || ReachesSeed(pair.Second);
        }
        return needed;
    }

    /// <summary>Union-find with path halving; single-threaded.</summary>
    private sealed class DisjointSets
    {
        private readonly int[] _parent;

        public DisjointSets(int count)
        {
            _parent = new int[count];
            for (var i = 0; i < count; i++) _parent[i] = i;
        }

        public int Find(int node)
        {
            while (_parent[node] != node)
            {
                _parent[node] = _parent[_parent[node]];
                node = _parent[node];
            }
            return node;
        }

        public void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB) _parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
        }
    }
}
