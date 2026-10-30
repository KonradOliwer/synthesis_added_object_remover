namespace AddedObjectRemover.Tests.IndexGraphQueries;

public class IndexGraphQueriesTests
{
    [Fact]
    public void UnionFindKeepsTheSmallerRootWhateverTheJoinOrder()
    {
        var roots = new UnionFind(6);
        roots.Join(5, 3);
        roots.Join(3, 4);
        roots.Join(4, 1);

        Assert.Equal([0, 1, 2, 1, 1, 1], Enumerable.Range(0, 6).Select(roots.Find));
    }

    [Fact]
    public void ComponentsAreNumberedByTheirLowestMemberAndListMembersAscending()
    {
        var components = Components.Of(7, [(6, 2), (4, 1), (2, 5)]);

        Assert.Equal([[0], [1, 4], [2, 5, 6], [3]], components);
    }

    [Fact]
    public void ComponentsDoNotDependOnTheEdgeOrder()
    {
        (int, int)[] edges = [(6, 2), (4, 1), (2, 5), (1, 4)];

        Assert.Equal(Components.Of(7, edges), Components.Of(7, edges.Reverse()));
    }

    [Fact]
    public void ComponentsOfNothingAreEmptyAndWithoutEdgesAreSingles()
    {
        Assert.Empty(Components.Of(0, []));
        Assert.Equal([[0], [1], [2]], Components.Of(3, []));
    }

    [Fact]
    public void ParentChainRunsFromTheRootDownToTheNode()
    {
        int[] parentOf = [-1, 0, 1, -1, 2];

        Assert.Equal([0, 1, 2, 4], ParentChains.ToRoot(4, parentOf));
    }

    [Fact]
    public void ParentChainOfARootIsTheRootAlone() =>
        Assert.Equal([3], ParentChains.ToRoot(3, [-1, 0, 1, -1]));

    [Fact]
    public void AdjacencyPairsListEachPairOnceFromItsSmallerEndInNodeThenNeighbourOrder()
    {
        int[] nodes = [4, 1, 7];
        IReadOnlyList<int>[] neighbors = [[7, 1, 9], [4, 7], [4, 1]];

        var pairs = AdjacencyPairs.From(nodes, neighbors, (_, _) => true, (first, second) => (first, second));

        Assert.Equal([(4, 7), (4, 9), (1, 4), (1, 7)], pairs);
    }

    [Fact]
    public void InvertedListsGiveEachIdTheNodesListingItInAscendingOrder()
    {
        IReadOnlyList<int>[] idsOfNode = [[2, 0], [], [3, 2, 0], [2]];

        var inverted = InvertedLists.Of(4, idsOfNode);

        Assert.Equal([0, 2], inverted.NodesListing(0).ToArray());
        Assert.Equal([0, 2, 3], inverted.NodesListing(2).ToArray());
        Assert.Equal([2], inverted.NodesListing(3).ToArray());
    }

    [Fact]
    public void InvertedListsAreEmptyForIdsNobodyListsAndForNoNodes()
    {
        var inverted = InvertedLists.Of(3, [[1], []]);
        var withoutNodes = InvertedLists.Of(2, []);

        Assert.True(inverted.NodesListing(0).IsEmpty);
        Assert.True(inverted.NodesListing(2).IsEmpty);
        Assert.True(withoutNodes.NodesListing(0).IsEmpty);
        Assert.True(withoutNodes.NodesListing(1).IsEmpty);
    }

    [Fact]
    public void AnIdANodeListsTwiceCountsTwice()
    {
        var inverted = InvertedLists.Of(2, [[1, 1]]);

        Assert.Equal([0, 0], inverted.NodesListing(1).ToArray());
    }

    [Fact]
    public void AdjacencyPairsSkipWhatTheFilterRejects()
    {
        int[] nodes = [1, 2];
        IReadOnlyList<int>[] neighbors = [[2, 3], [3]];

        var pairs = AdjacencyPairs.From(nodes, neighbors, (_, second) => second != 3, (first, second) => (first, second));

        Assert.Equal([(1, 2)], pairs);
    }
}
