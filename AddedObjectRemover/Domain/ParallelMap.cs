using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Work counts that <see cref="ParallelMap"/> sums; the sum must be associative and commutative.</summary>
internal interface IWork<TSelf> where TSelf : IWork<TSelf>
{
    static abstract TSelf Zero { get; }

    static abstract TSelf operator +(TSelf first, TSelf second);
}

/// <summary>
/// The only way steps run work in parallel. Result i is always body(i), whatever the order or the
/// number of workers, and work counts are summed with an associative and commutative add, so
/// neither depends on scheduling. The order only groups items that share cached data.
/// </summary>
internal static class ParallelMap
{
    /// <summary>With the automatic range size, each worker gets about this many ranges, so a slow range is rebalanced.</summary>
    private const int RangesPerWorker = 16;

    /// <summary>For items that are slow and uneven in cost, so each is handed out on its own.</summary>
    public const int OneItemPerRange = 1;

    /// <summary>The <see cref="ParallelOptions.MaxDegreeOfParallelism"/> that means no limit besides the machine's.</summary>
    private const int UnlimitedWorkers = -1;

    /// <param name="order">A permutation of the item indexes, in the order they are started.</param>
    /// <param name="harvest">Reads a scratch's work counts once, after its worker is done.</param>
    /// <param name="rangeSize">Items per range handed to a worker; null chooses automatically.</param>
    public static (TOut[] Results, TWork Work) Run<TScratch, TOut, TWork>(
        ParallelOptions exec,
        ImmutableArray<int> order,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest,
        int? rangeSize = null)
        where TWork : IWork<TWork>
    {
        Permutation.Require(order, nameof(order));
        return RunSummingWork(exec, order.Length, position => order[position], newScratch, body, harvest, rangeSize);
    }

    /// <summary>Runs one item per target, in the work order; throws unless the order covers exactly <paramref name="targetCount"/> targets.</summary>
    public static (TOut[] Results, TWork Work) Run<TScratch, TOut, TWork>(
        ParallelOptions exec,
        WorkOrder order,
        int targetCount,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest)
        where TWork : IWork<TWork>
    {
        order.RequireCovers(targetCount);
        return Run(exec, order.TargetsBySpaceAndCell, newScratch, body, harvest);
    }

    /// <summary>Runs one item per target, in the work order; throws unless the order covers exactly <paramref name="targetCount"/> targets.</summary>
    public static TOut[] Run<TScratch, TOut>(
        ParallelOptions exec, WorkOrder order, int targetCount, Func<TScratch> newScratch, Func<int, TScratch, TOut> body)
    {
        order.RequireCovers(targetCount);
        return Run(exec, order.TargetsBySpaceAndCell, newScratch, body);
    }

    /// <summary>Starts the items in index order.</summary>
    public static (TOut[] Results, TWork Work) Run<TScratch, TOut, TWork>(
        ParallelOptions exec,
        int count,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest,
        int? rangeSize = null)
        where TWork : IWork<TWork> =>
        RunSummingWork(exec, count, static position => position, newScratch, body, harvest, rangeSize);

    /// <param name="order">A permutation of the item indexes, in the order they are started.</param>
    public static TOut[] Run<TScratch, TOut>(
        ParallelOptions exec, ImmutableArray<int> order, Func<TScratch> newScratch, Func<int, TScratch, TOut> body, int? rangeSize = null)
    {
        Permutation.Require(order, nameof(order));
        return Map(exec, order.Length, position => order[position], newScratch, body, rangeSize, finish: static _ => { });
    }

    public static TOut[] Run<TScratch, TOut>(
        ParallelOptions exec, int count, Func<TScratch> newScratch, Func<int, TScratch, TOut> body, int? rangeSize = null) =>
        Map(exec, count, static position => position, newScratch, body, rangeSize, finish: static _ => { });

    public static TOut[] Run<TOut>(ParallelOptions exec, int count, Func<int, TOut> body, int? rangeSize = null) =>
        Map<object?, TOut>(exec, count, static position => position, static () => null, (item, _) => body(item), rangeSize, finish: static _ => { });

    private static (TOut[] Results, TWork Work) RunSummingWork<TScratch, TOut, TWork>(
        ParallelOptions exec,
        int count,
        Func<int, int> itemAt,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest,
        int? rangeSize)
        where TWork : IWork<TWork>
    {
        var work = TWork.Zero;
        var workLock = new object();
        var results = Map(exec, count, itemAt, newScratch, body, rangeSize, scratch =>
        {
            var harvested = harvest(scratch);
            lock (workLock) work += harvested;
        });
        return (results, work);
    }

    private static TOut[] Map<TScratch, TOut>(
        ParallelOptions exec,
        int count,
        Func<int, int> itemAt,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        int? rangeSize,
        Action<TScratch> finish)
    {
        var results = new TOut[count];
        if (count == 0) return results;
        Parallel.ForEach(
            Partitioner.Create(0, count, ChooseRangeSize(exec, count, rangeSize)),
            exec,
            newScratch,
            (range, _, scratch) =>
            {
                for (var position = range.Item1; position < range.Item2; position++)
                {
                    var item = itemAt(position);
                    results[item] = body(item, scratch);
                }
                return scratch;
            },
            finish);
        return results;
    }

    private static int ChooseRangeSize(ParallelOptions exec, int count, int? rangeSize)
    {
        if (rangeSize is { } given) return given;
        var workers = exec.MaxDegreeOfParallelism == UnlimitedWorkers ? Environment.ProcessorCount : exec.MaxDegreeOfParallelism;
        return Math.Max(1, count / (workers * RangesPerWorker));
    }
}
