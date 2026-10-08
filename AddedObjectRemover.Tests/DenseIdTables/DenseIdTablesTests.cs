namespace AddedObjectRemover.Tests.DenseIdTables;

public class DenseIdTablesTests
{
    [Fact]
    public void IdSetContainsExactlyTheGivenIds()
    {
        var set = IdSet.Of(5, [1, 3, 3]);

        Assert.Equal([false, true, false, true, false], Enumerable.Range(0, 5).Select(set.Contains));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(1000)]
    public void IdSetDoesNotContainIdsOutsideTheCount(int id) =>
        Assert.False(IdSet.Of(5, [0, 4]).Contains(id));

    [Fact]
    public void IdSetOfNothingIsEmpty() =>
        Assert.False(IdSet.Of(0, []).Contains(0));

    [Fact]
    public void IdSetRejectsAnIdBeyondTheCount() =>
        Assert.ThrowsAny<Exception>(() => IdSet.Of(2, [2]));

    [Fact]
    public void InOrderSortsByKeyAndNumbersFromTheFirstId()
    {
        string[] items = ["c", "a", "b"];

        var numbered = DenseNumbering.InOrder(items, item => item, StringComparer.Ordinal, 10, (item, id) => $"{item}{id}");

        Assert.Equal(["a10", "b11", "c12"], numbered.AsEnumerable());
    }

    [Fact]
    public void InOrderKeepsTheInputOrderOfEqualKeys()
    {
        (string Name, int Rank)[] items = [("first", 1), ("second", 0), ("third", 1), ("fourth", 0)];

        var numbered = DenseNumbering.InOrder(items, item => item.Rank, Comparer<int>.Default, 0, (item, id) => (item.Name + id, item.Rank));

        Assert.Equal(["second0", "fourth1", "first2", "third3"], numbered.Select(item => item.Item1));
    }

    [Fact]
    public void InOrderOfNothingGivesNothing() =>
        Assert.Empty(DenseNumbering.InOrder(Array.Empty<int>(), item => item, Comparer<int>.Default, 5, (item, _) => item));

    [Fact]
    public void PerIndexTableAnswersEachGivenIndexAndNoOther()
    {
        var table = PerIndexTable<string>.From(["two", "zero", "five"], item => item switch { "zero" => 0, "two" => 2, _ => 5 });

        Assert.Equal([true, false, true, false, false, true, false], Enumerable.Range(0, 7).Select(table.Has));
        Assert.Equal("zero", table.Get(0));
        Assert.Equal("five", table.Get(5));
        Assert.True(table.TryGet(2, out var two));
        Assert.Equal("two", two);
        Assert.False(table.TryGet(1, out _));
        Assert.False(table.TryGet(-1, out _));
        Assert.False(table.Has(100));
    }

    [Fact]
    public void PerIndexTableOfItemsHoldsTheirValuesAtTheirIndices()
    {
        var table = PerIndexTable<string>.From(new[] { (Index: 4, Name: "four"), (Index: 1, Name: "one") }, item => item.Index, item => item.Name);

        Assert.Equal("four", table.Get(4));
        Assert.Equal("one", table.Get(1));
        Assert.False(table.Has(0));
    }

    [Fact]
    public void PerIndexTableGetOfAMissingIndexFails()
    {
        var table = PerIndexTable<string>.From(["a"], _ => 3);

        Assert.Throws<KeyNotFoundException>(() => table.Get(2));
    }

    [Fact]
    public void PerIndexTableRejectsTwoItemsWithOneIndex() =>
        Assert.Throws<ArgumentException>(() => PerIndexTable<string>.From(["a", "b"], _ => 1));

    [Fact]
    public void PerIndexTableOfNothingHasNoIndex()
    {
        var table = PerIndexTable<string>.From([], _ => 0);

        Assert.False(table.Has(0));
    }
}
