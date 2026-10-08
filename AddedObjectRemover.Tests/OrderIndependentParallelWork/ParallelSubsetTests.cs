using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.ParallelWork;

public class ParallelSubsetTests
{
    private static readonly int[] WorkerCounts = [1, 2, 4, 8];

    private readonly record struct Tests(int Count) : IWork<Tests>
    {
        public static Tests Zero => default;

        public static Tests operator +(Tests first, Tests second) => new(first.Count + second.Count);
    }

    private sealed class TestCounter
    {
        public int Count;
    }

    [Fact]
    public void RunOverGivesOneResultPerIndexInTheGivenSubsetOrder()
    {
        var order = new WorkOrder([3, 0, 4, 1, 2]);

        var results = ParallelMap.RunOver(new Execution(4), order, [4, 1, 3], () => 0, (index, _) => index * 10, ParallelMap.AutomaticRangeSize);

        Assert.Equal([40, 10, 30], results);
    }

    [Fact]
    public void RunOverOfNoIndicesGivesNoResults()
    {
        var order = new WorkOrder([0, 1]);

        Assert.Empty(ParallelMap.RunOver(new Execution(4), order, [], () => 0, (index, _) => index, ParallelMap.AutomaticRangeSize));
    }

    [Fact]
    public void RunOverRejectsAnIndexTheOrderWasNotBuiltFor()
    {
        var order = new WorkOrder([0, 1]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParallelMap.RunOver(new Execution(2), order, [0, 2], () => 0, (index, _) => index, ParallelMap.AutomaticRangeSize));
    }

    [Fact]
    public void RunOverGivesTheSameResultsWhateverTheWorkersOrTheOrder()
    {
        const int count = 200;
        int[] subset = [.. Enumerable.Range(0, count).Where(index => index % 3 != 0)];
        var forward = new WorkOrder([.. Enumerable.Range(0, count)]);
        var backward = new WorkOrder([.. Enumerable.Range(0, count).Reverse()]);
        var expected = subset.Select(Describe).ToArray();

        foreach (var workers in WorkerCounts)
        {
            Assert.Equal(expected, ParallelMap.RunOver(
                new Execution(workers), forward, subset, () => 0, (index, _) => Describe(index), ParallelMap.AutomaticRangeSize));
            Assert.Equal(expected, ParallelMap.RunOver(
                new Execution(workers), backward, subset, () => 0, (index, _) => Describe(index), ParallelMap.AutomaticRangeSize));
        }
    }

    [Fact]
    public void FirstMatchPerGroupTakesTheFirstMatchInGroupOrderAndStopsThere()
    {
        IReadOnlyList<int>[] groups = [[5, 2, 9, 4], [7, 1], [8, 6, 3]];

        var (matches, work) = FindFirstMatches(groups, isMatch: item => item % 2 == 0, workers: 1);

        Assert.Equal([2, 8], matches);
        Assert.Equal(new Tests(2 + 2 + 1), work);
    }

    [Fact]
    public void FirstMatchPerGroupMeansFirstInInputOrderNotFirstToFinish()
    {
        IReadOnlyList<int>[] groups = [[1, 2, 3]];

        foreach (var workers in WorkerCounts)
        {
            var (matches, _) = ParallelMap.FirstMatchPerGroup(
                new Execution(workers),
                groups,
                () => new TestCounter(),
                (item, _) =>
                {
                    if (item == 2) Thread.Sleep(20);
                    return item >= 2;
                },
                scratch => new Tests(scratch.Count),
                ParallelMap.AutomaticRangeSize);

            Assert.Equal([2], matches);
        }
    }

    [Fact]
    public void FirstMatchPerGroupGivesMatchesAscendingWhateverTheGroupOrder()
    {
        IReadOnlyList<int>[] groups = [[9, 8], [3, 2], [6, 5]];

        var (matches, _) = FindFirstMatches(groups, isMatch: _ => true, workers: 4);

        Assert.Equal([3, 6, 9], matches);
    }

    [Fact]
    public void FirstMatchPerGroupOfNoGroupsOrNoMatchesGivesNothing()
    {
        Assert.Empty(FindFirstMatches([], isMatch: _ => true, workers: 4).Matches);
        Assert.Empty(FindFirstMatches([[1, 2], [3]], isMatch: _ => false, workers: 4).Matches);
    }

    [Fact]
    public void FirstMatchPerGroupGivesTheSameMatchesAndWorkWhateverTheWorkers()
    {
        IReadOnlyList<int>[] groups = [.. Enumerable.Range(0, 60).Select(group => (IReadOnlyList<int>)[.. Enumerable.Range(0, 1 + group % 7).Select(offset => group * 10 + offset)])];
        bool IsMatch(int item) => item % 4 == 3;
        var expected = FindFirstMatches(groups, IsMatch, workers: 1);

        foreach (var workers in WorkerCounts)
        {
            var actual = FindFirstMatches(groups, IsMatch, workers);

            Assert.Equal(expected.Matches, actual.Matches);
            Assert.Equal(expected.Work, actual.Work);
        }
    }

    [Fact]
    public void WorkSumAddsAllCountsAndGivesZeroForNone()
    {
        Assert.Equal(new Tests(6), Work.Sum([new Tests(1), new Tests(2), new Tests(3)]));
        Assert.Equal(Tests.Zero, Work.Sum(Array.Empty<Tests>()));
    }

    [Fact]
    public void AtomicCounterCountsEveryIncrementFromManyThreadsAndHandsOutDistinctNumbers()
    {
        const int increments = 4000;
        var counter = new AtomicCounter();
        var numbers = new int[increments];

        ParallelMap.Run(new Execution(8), increments, index => numbers[index] = counter.Increment(), rangeSize: 7);

        Assert.Equal(increments, counter.Value);
        Assert.Equal(Enumerable.Range(1, increments), numbers.Order());
    }

    private static (List<int> Matches, Tests Work) FindFirstMatches(IReadOnlyList<IReadOnlyList<int>> groups, Func<int, bool> isMatch, int workers) =>
        ParallelMap.FirstMatchPerGroup(
            new Execution(workers),
            groups,
            () => new TestCounter(),
            (item, scratch) =>
            {
                scratch.Count++;
                return isMatch(item);
            },
            scratch => new Tests(scratch.Count),
            rangeSize: ParallelMap.OneItemPerRange);

    private static string Describe(int index) => $"item {index}";
}
