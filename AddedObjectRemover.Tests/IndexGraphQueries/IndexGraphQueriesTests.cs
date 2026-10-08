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
    public void SetClosureSpreadsThroughGroupsThatShareAnItemWithTheResult()
    {
        IReadOnlyCollection<int>[] groups = [new HashSet<int> { 3, 4 }, new HashSet<int> { 1, 2 }, new HashSet<int> { 2, 3 }, new HashSet<int> { 8, 9 }];

        var closure = SetClosure.Of([1], groups, EqualityComparer<int>.Default);

        Assert.Equal([1, 2, 3, 4], closure.Order());
    }

    [Fact]
    public void SetClosureOfSeedsWithoutOverlappingGroupsIsTheSeeds()
    {
        IReadOnlyCollection<string>[] groups = [new HashSet<string> { "x", "y" }];

        var closure = SetClosure.Of(["a"], groups, StringComparer.Ordinal);

        Assert.Equal(["a"], closure);
    }

    [Fact]
    public void SetClosureUsesTheGivenComparer()
    {
        IReadOnlyCollection<string>[] groups = [new HashSet<string> { "A", "B" }];

        var closure = SetClosure.Of(["a"], groups, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(2, closure.Count);
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
    public void AdjacencyPairsSkipWhatTheFilterRejects()
    {
        int[] nodes = [1, 2];
        IReadOnlyList<int>[] neighbors = [[2, 3], [3]];

        var pairs = AdjacencyPairs.From(nodes, neighbors, (_, second) => second != 3, (first, second) => (first, second));

        Assert.Equal([(1, 2)], pairs);
    }
}
