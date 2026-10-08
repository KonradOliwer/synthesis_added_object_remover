namespace AddedObjectRemover.Tests.ParallelWork;

public class ParallelResultsTests
{
    private static readonly int[] WorkerCounts = [1, 2, 4, 8];

    [Fact]
    public void CompactKeepsTheReferenceResultsThatAreNotNullInIndexOrder()
    {
        string?[] results = ["a", null, "b", null, null, "c"];

        Assert.Equal(["a", "b", "c"], ParallelResults.Compact(results));
    }

    [Fact]
    public void CompactKeepsTheValueResultsThatAreNotNullInIndexOrder()
    {
        int?[] results = [null, 5, 0, null, 3];

        Assert.Equal([5, 0, 3], ParallelResults.Compact(results));
    }

    [Fact]
    public void CompactOfNothingIsEmpty()
    {
        Assert.Empty(ParallelResults.Compact(Array.Empty<string?>()));
        Assert.Empty(ParallelResults.Compact(new int?[] { null, null }));
    }

    [Fact]
    public void IndicesWhereListsTheAcceptedIndicesAscending()
    {
        int[] results = [4, 7, 2, 9, 6];

        Assert.Equal([1, 3], ParallelResults.IndicesWhere(results, result => result > 6));
    }

    [Fact]
    public void IndicesWhereOfNothingAcceptedIsEmpty()
    {
        Assert.Empty(ParallelResults.IndicesWhere(new[] { 1, 2 }, _ => false));
        Assert.Empty(ParallelResults.IndicesWhere(Array.Empty<int>(), _ => true));
    }

    [Fact]
    public void CompactingAMapGivesTheSameResultsForEveryWorkerCount()
    {
        const int count = 500;
        var expected = ParallelResults.Compact(Enumerable.Range(0, count).Select(Keep).ToArray());

        foreach (var workers in WorkerCounts)
        {
            var results = ParallelMap.Run(new Execution(workers), count, Keep, rangeSize: 3);
            Assert.Equal(expected, ParallelResults.Compact(results));
        }
    }

    [Fact]
    public void IndicesWhereOfAMapGivesTheSameIndicesForEveryWorkerCount()
    {
        const int count = 500;
        var expected = ParallelResults.IndicesWhere(Enumerable.Range(0, count).Select(Keep).ToArray(), kept => kept != null);

        foreach (var workers in WorkerCounts)
        {
            var results = ParallelMap.Run(new Execution(workers), count, Keep, rangeSize: 3);
            Assert.Equal(expected, ParallelResults.IndicesWhere(results, kept => kept != null));
        }
    }

    private static int? Keep(int index) => index % 3 == 0 ? null : index * 2;
}
