namespace AddedObjectRemover.Tests.Grouping;

public class KeyedGroupsTests
{
    [Fact]
    public void IndicesByKeyListsGroupsInFirstSeenOrderWithAscendingIndices()
    {
        string[] items = ["b1", "a1", "b2", "c1", "a2", "b3"];

        var groups = KeyedGroups.IndicesByKey(items, item => item[0], EqualityComparer<char>.Default);

        Assert.Equal(['b', 'a', 'c'], groups.Select(group => group.Key));
        Assert.Equal([0, 2, 5], groups[0].Indices);
        Assert.Equal([1, 4], groups[1].Indices);
        Assert.Equal([3], groups[2].Indices);
    }

    [Fact]
    public void IndicesByKeyUsesTheComparerAndKeepsTheFirstSpelling()
    {
        string[] items = ["Mesh.nif", "other.nif", "MESH.NIF"];

        var groups = KeyedGroups.IndicesByKey(items, item => item, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(["Mesh.nif", "other.nif"], groups.Select(group => group.Key));
        Assert.Equal([0, 2], groups[0].Indices);
    }

    [Fact]
    public void IndicesByKeyOfNothingGivesNoGroups() =>
        Assert.Empty(KeyedGroups.IndicesByKey(Array.Empty<int>(), item => item, EqualityComparer<int>.Default));

    [Fact]
    public void DistinctByKeepsTheFirstItemOfEachKeyInInputOrder()
    {
        string[] items = ["b1", "a1", "b2", "c1", "a2"];

        Assert.Equal(["b1", "a1", "c1"], KeyedGroups.DistinctBy(items, item => item[0], EqualityComparer<char>.Default));
    }

    [Fact]
    public void CountByCountsEachKeyInFirstSeenOrder()
    {
        string[] items = ["b1", "a1", "b2", "c1", "a2", "b3"];

        var counts = KeyedGroups.CountBy(items, item => item[0], EqualityComparer<char>.Default);

        Assert.Equal([KeyValuePair.Create('b', 3), KeyValuePair.Create('a', 2), KeyValuePair.Create('c', 1)], counts);
    }

    [Fact]
    public void CountByUsesTheComparerAndKeepsTheFirstSpelling()
    {
        string[] items = ["Mesh", "other", "MESH"];

        var counts = KeyedGroups.CountBy(items, item => item, StringComparer.OrdinalIgnoreCase);

        Assert.Equal([KeyValuePair.Create("Mesh", 2), KeyValuePair.Create("other", 1)], counts);
    }

    [Fact]
    public void CountByOfNothingGivesNoCounts() =>
        Assert.Empty(KeyedGroups.CountBy(Array.Empty<int>(), item => item, EqualityComparer<int>.Default));

    [Fact]
    public void RankOrdersByCountDescendingThenByTheKeyComparer()
    {
        KeyValuePair<string, int>[] counts = [new("b", 1), new("c", 3), new("a", 1), new("d", 3)];

        var ranked = KeyedGroups.Rank(counts, StringComparer.Ordinal);

        Assert.Equal(["c", "d", "a", "b"], ranked.Select(entry => entry.Key));
    }

    [Fact]
    public void RankUsesTheGivenKeyComparerForTies()
    {
        KeyValuePair<string, int>[] counts = [new("b", 1), new("B", 1), new("A", 1)];

        Assert.Equal(["A", "B", "b"], KeyedGroups.Rank(counts, StringComparer.Ordinal).Select(entry => entry.Key));
        Assert.Equal(["A", "b", "B"], KeyedGroups.Rank(counts, StringComparer.OrdinalIgnoreCase).Select(entry => entry.Key));
    }

    [Fact]
    public void GetOrAddListStoresAnEmptyListOnceAndReturnsTheSameListAfterwards()
    {
        var lists = new Dictionary<string, List<int>>();

        var first = KeyedGroups.GetOrAddList(lists, "a");
        first.Add(1);
        var second = KeyedGroups.GetOrAddList(lists, "a");

        Assert.Same(first, second);
        Assert.Equal([1], lists["a"]);
        Assert.Empty(KeyedGroups.GetOrAddList(lists, "b"));
        Assert.Equal(2, lists.Count);
    }

    [Fact]
    public void ArgMaxFirstWinsKeepsTheFirstItemOfTheGreatestValue()
    {
        string[] items = ["b1", "a3", "c2", "d3"];

        Assert.Equal("a3", KeyedGroups.ArgMaxFirstWins(items, item => item[1], Comparer<char>.Default));
    }

    [Fact]
    public void ArgMaxFirstWinsUsesTheGivenComparer()
    {
        string[] items = ["a1", "b3", "c2"];

        Assert.Equal("a1", KeyedGroups.ArgMaxFirstWins(items, item => item[1], Comparer<char>.Create((x, y) => y.CompareTo(x))));
    }

    [Fact]
    public void ArgMaxFirstWinsOfOneItemGivesThatItem() =>
        Assert.Equal(7, KeyedGroups.ArgMaxFirstWins([7], item => item, Comparer<int>.Default));

    [Fact]
    public void DistinctByUsesTheComparer()
    {
        string[] items = ["Mesh", "MESH", "other"];

        Assert.Equal(["Mesh", "other"], KeyedGroups.DistinctBy(items, item => item, StringComparer.OrdinalIgnoreCase));
    }
}
