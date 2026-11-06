using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Determinism;

public class ParallelMapTests
{
    private const int ItemCount = 1000;
    private const int ShuffleSeed = 7;
    private const int OtherShuffleSeed = 11;
    private const int LowerFailingItem = 3;
    private const int HigherFailingItem = 7;

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

        var results = ParallelMap.Run(Workers(8), order, () => new CountingScratch(), (item, _) => Square(item), ParallelMap.AutomaticRangeSize);

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

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void RunOnEachGivesEachItemsResultAtItsPlace(int workers)
    {
        List<int> items = [.. Shuffled(ItemCount)];

        var results = ParallelMap.RunOnEach(Workers(workers), items, () => new CountingScratch(), (item, _) => Square(item));

        Assert.Equal(items.Select(Square), results);
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
        Assert.Empty(ParallelMap.Run(Workers(8), 0, item => item, ParallelMap.AutomaticRangeSize));
    }

    [Fact]
    public void AFailingBodySurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run<int>(Workers(8), ItemCount, item => item == ItemCount / 2 ? throw new InvalidOperationException() : item, ParallelMap.AutomaticRangeSize));

    [Fact]
    public void SeveralFailingItemsReportTheOneWithTheLowestIndexWhateverTheWorkers()
    {
        const int Repeats = 20;
        const int LowestFailing = 100;

        for (var repeat = 0; repeat < Repeats; repeat++)
        {
            var failure = Assert.Throws<AggregateException>(() => ParallelMap.Run<int>(
                Workers(8),
                ItemCount,
                item => item >= LowestFailing ? throw new InvalidOperationException($"item {item}") : item,
                ParallelMap.OneItemPerRange));

            Assert.Equal($"item {LowestFailing}", Assert.Single(failure.InnerExceptions).Message);
        }
    }

    [Fact]
    public void AnOutOfMemoryFailureIsReportedBeforeALowerFailure()
    {
        var failure = Assert.Throws<AggregateException>(() => ParallelMap.Run<int>(
            Workers(1),
            ItemCount,
            item => item switch
            {
                1 => throw new InvalidOperationException(),
                2 => throw new OutOfMemoryException(),
                _ => item,
            },
            ParallelMap.OneItemPerRange));

        Assert.IsType<OutOfMemoryException>(Assert.Single(failure.InnerExceptions));
    }

    [Theory]
    [InlineData(1, ShuffleSeed)]
    [InlineData(1, OtherShuffleSeed)]
    [InlineData(8, ShuffleSeed)]
    [InlineData(8, OtherShuffleSeed)]
    public void ABugIsReportedBeforeALowerItemsUserDataFailureWhateverTheWorkersAndOrder(int workers, int seed)
    {
        var failure = Assert.Throws<AggregateException>(() => RunFailing(workers, seed, () => new IOException(), () => new NullReferenceException()));

        Assert.IsType<NullReferenceException>(Assert.Single(failure.InnerExceptions));
        Assert.False(Failures.IsRecoverable(failure));
    }

    [Theory]
    [InlineData(1, ShuffleSeed)]
    [InlineData(1, OtherShuffleSeed)]
    [InlineData(8, ShuffleSeed)]
    [InlineData(8, OtherShuffleSeed)]
    public void OnlyUserDataFailuresReportTheLowestItemsWhateverTheWorkersAndOrder(int workers, int seed)
    {
        var failure = Assert.Throws<AggregateException>(() => RunFailing(workers, seed, () => new IOException(), () => new PlantedUserDataException()));

        Assert.IsType<IOException>(Assert.Single(failure.InnerExceptions));
        Assert.True(Failures.IsRecoverable(failure));
    }

    [Fact]
    public void ARunOverAWorkOrderThatDoesNotCoverTheTargetsIsRefused()
    {
        var order = new WorkOrder([0, 1]);

        Assert.Throws<ArgumentException>(
            () => ParallelMap.Run(Workers(2), order, 3, () => 0, (item, _) => item, ParallelMap.AutomaticRangeSize));
        Assert.Throws<ArgumentException>(
            () => ParallelMap.Run(Workers(2), order, 3, () => 0, (item, _) => item, _ => new Work(0, 0), ParallelMap.AutomaticRangeSize));
        Assert.Throws<ArgumentException>(
            () => ParallelMap.Run(Workers(2), order, 3, item => item, ParallelMap.AutomaticRangeSize));
    }

    [Fact]
    public void AFailingScratchCreationSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run<int, long, Work>(
            Workers(4), ItemCount, () => throw new InvalidOperationException(), (item, _) => item, _ => default, ParallelMap.AutomaticRangeSize));

    [Fact]
    public void AFailingHarvestSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run<int, long, Work>(
            Workers(4), ItemCount, () => 0, (item, _) => item, _ => throw new InvalidOperationException(), ParallelMap.AutomaticRangeSize));

    [Fact]
    public void AFailingBodyInACountingRunSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run(
            Workers(4),
            ItemCount,
            () => new CountingScratch(),
            (item, _) => item == ItemCount / 2 ? throw new InvalidOperationException() : item,
            scratch => new Work(scratch.ItemsSeen, scratch.IndexSum),
            ParallelMap.AutomaticRangeSize));

    [Fact]
    public void AFailingSumOfTheWorkSurfacesAsAnAggregateException() =>
        AssertSurfaces(() => ParallelMap.Run(
            Workers(4), ItemCount, () => 0, (item, _) => item, _ => new ThrowingWork(), ParallelMap.AutomaticRangeSize));

    private readonly record struct ThrowingWork : IWork<ThrowingWork>
    {
        public static ThrowingWork Zero => default;

        public static ThrowingWork operator +(ThrowingWork first, ThrowingWork second) => throw new InvalidOperationException();
    }

    [Theory]
    [InlineData(1, ParallelMap.AutomaticRangeSize)]
    [InlineData(1, 1)]
    [InlineData(1, 5000)]
    [InlineData(1000, 7)]
    [InlineData(1000, 1000000)]
    public void AnyRangeSizeGivesTheSameResultsAndWork(int count, int rangeSize)
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
            scratch => new Work(scratch.ItemsSeen, scratch.IndexSum),
            ParallelMap.AutomaticRangeSize);

    /// <summary>Every item runs in a shuffled order; the lower failing item throws the first failure, the higher one the second.</summary>
    private static int[] RunFailing(int workers, int seed, Func<Exception> lowerFailure, Func<Exception> higherFailure) =>
        ParallelMap.Run(
            Workers(workers),
            Shuffled(ItemCount, seed),
            () => 0,
            (item, _) => item switch
            {
                LowerFailingItem => throw lowerFailure(),
                HigherFailingItem => throw higherFailure(),
                _ => item,
            },
            ParallelMap.OneItemPerRange);

    private static long Square(int item) => (long)item * item;

    private static ImmutableArray<int> Shuffled(int count, int seed = ShuffleSeed)
    {
        var order = Enumerable.Range(0, count).ToArray();
        new Random(seed).Shuffle(order);
        return [.. order];
    }

    private static Execution Workers(int count) => new(count);
}
