using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Determinism;

public class ParallelMapTests
{
    private const int ItemCount = 1000;
    private const int ShuffleSeed = 7;

    private sealed class CountingScratch
    {
        public long ItemsSeen;
        public long IndexSum;
    }

    private readonly record struct Work(long Items, long IndexSum) : IWork<Work>
    {
        public static Work Zero => default;

        public static Work operator +(Work first, Work second) => new(first.Items + second.Items, first.IndexSum + second.IndexSum);
    }

    [Fact]
    public void ResultsLandByIndexUnderAShuffledOrder()
    {
        var order = Shuffled(ItemCount);

        var results = ParallelMap.Run(Workers(8), order, () => new CountingScratch(), (item, _) => Square(item));

        Assert.Equal(Enumerable.Range(0, ItemCount).Select(Square), results);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void AnyWorkerCountGivesTheSequentialResultsAndWork(int workers)
    {
        var (results, work) = RunCounting(Workers(workers), Shuffled(ItemCount));

        Assert.Equal(Enumerable.Range(0, ItemCount).Select(Square), results);
        Assert.Equal(new Work(ItemCount, Enumerable.Range(0, ItemCount).Sum(item => (long)item)), work);
    }

    [Fact]
    public void OneWorkerReusesOneScratchForAllItems()
    {
        var scratchesCreated = 0;

        ParallelMap.Run(Workers(1), ItemCount, () => Interlocked.Increment(ref scratchesCreated), (item, _) => item, rangeSize: ParallelMap.OneItemPerRange);

        Assert.Equal(1, scratchesCreated);
    }

    [Theory]
    [InlineData(new[] { 0, 1, 1 })]
    [InlineData(new[] { 0, 1, 3 })]
    [InlineData(new[] { -1, 0, 1 })]
    public void AnOrderThatIsNotAPermutationThrows(int[] order) =>
        Assert.Throws<ArgumentException>(() => RunCounting(Workers(1), [.. order]));

    [Fact]
    public void NoItemsGiveNoResultsAndZeroWork()
    {
        var (results, work) = RunCounting(Workers(8), []);

        Assert.Empty(results);
        Assert.Equal(default, work);
        Assert.Empty(ParallelMap.Run(Workers(8), 0, item => item));
    }

    [Fact]
    public void AFailingBodySurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run<int>(Workers(8), ItemCount, item => item == ItemCount / 2 ? throw new InvalidOperationException() : item));

    [Fact]
    public void ARunOverAWorkOrderThatDoesNotCoverTheTargetsIsRefused()
    {
        var order = new WorkOrder([0, 1]);

        Assert.Throws<ArgumentException>(() => ParallelMap.Run(Workers(2), order, 3, () => 0, (item, _) => item));
        Assert.Throws<ArgumentException>(() => ParallelMap.Run(Workers(2), order, 3, () => 0, (item, _) => item, _ => new Work(0, 0)));
    }

    [Fact]
    public void AFailingScratchCreationSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run<int, long, Work>(
            Workers(4), ItemCount, () => throw new InvalidOperationException(), (item, _) => item, _ => default));

    [Fact]
    public void AFailingHarvestSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run<int, long, Work>(
            Workers(4), ItemCount, () => 0, (item, _) => item, _ => throw new InvalidOperationException()));

    [Fact]
    public void AFailingBodyInACountingRunSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run(
            Workers(4),
            ItemCount,
            () => new CountingScratch(),
            (item, _) => item == ItemCount / 2 ? throw new InvalidOperationException() : item,
            scratch => new Work(scratch.ItemsSeen, scratch.IndexSum)));

    [Fact]
    public void AFailingSumOfTheWorkSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run(Workers(4), ItemCount, () => 0, (item, _) => item, _ => new ThrowingWork()));

    private readonly record struct ThrowingWork : IWork<ThrowingWork>
    {
        public static ThrowingWork Zero => default;

        public static ThrowingWork operator +(ThrowingWork first, ThrowingWork second) => throw new InvalidOperationException();
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(1, 1)]
    [InlineData(1, 5000)]
    [InlineData(1000, 7)]
    [InlineData(1000, 1000000)]
    public void AnyRangeSizeGivesTheSameResultsAndWork(int count, int? rangeSize)
    {
        var (expected, expectedWork) = RunCounting(Workers(1), [.. Enumerable.Range(0, count)]);

        var (results, work) = ParallelMap.Run(
            Workers(8),
            count,
            () => new CountingScratch(),
            (item, scratch) =>
            {
                scratch.ItemsSeen++;
                scratch.IndexSum += item;
                return Square(item);
            },
            scratch => new Work(scratch.ItemsSeen, scratch.IndexSum),
            rangeSize);

        Assert.Equal(expected, results);
        Assert.Equal(expectedWork, work);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(2, 100)]
    public void FewItemsPerWorkerStillRunEveryItemOnce(int workers, int count)
    {
        var (results, work) = RunCounting(Workers(workers), [.. Enumerable.Range(0, count)]);

        Assert.Equal(Enumerable.Range(0, count).Select(Square), results);
        Assert.Equal(count, work.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FewerThanOneWorkerIsRejected(int workers) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Execution(workers));

    private static void AssertSurfaces(Action run)
    {
        var failure = Assert.Throws<AggregateException>(run);
        Assert.Contains(failure.InnerExceptions, exception => exception is InvalidOperationException);
    }

    private static (long[] Results, Work Work) RunCounting(Execution execution, ImmutableArray<int> order) =>
        ParallelMap.Run(
            execution,
            order,
            () => new CountingScratch(),
            (item, scratch) =>
            {
                scratch.ItemsSeen++;
                scratch.IndexSum += item;
                return Square(item);
            },
            scratch => new Work(scratch.ItemsSeen, scratch.IndexSum));

    private static long Square(int item) => (long)item * item;

    private static ImmutableArray<int> Shuffled(int count)
    {
        var order = Enumerable.Range(0, count).ToArray();
        new Random(ShuffleSeed).Shuffle(order);
        return [.. order];
    }

    private static Execution Workers(int count) => new(count);
}
