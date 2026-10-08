using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Work counts that <see cref="ParallelMap"/> sums; the sum must be associative and commutative.</summary>
public interface IWork<TSelf> where TSelf : IWork<TSelf>
{
    static abstract TSelf Zero { get; }

    static abstract TSelf operator +(TSelf first, TSelf second);
}

/// <summary>
/// The only way steps run work in parallel. Result i is always body(i), whatever the order or the
/// number of workers, and work counts are summed with an associative and commutative add, so
/// neither depends on scheduling. The order only groups items that share cached data.
/// </summary>
public static class ParallelMap
{
    /// <summary>With the automatic range size, each worker gets about this many ranges, so a slow range is rebalanced.</summary>
    private const int RangesPerWorker = 16;

    /// <summary>For items that are slow and uneven in cost, so each is handed out on its own.</summary>
    public const int OneItemPerRange = 1;

    /// <summary>The range size that lets <see cref="ParallelMap"/> choose, for items of similar cost.</summary>
    public const int AutomaticRangeSize = 0;

    /// <param name="order">A permutation of the item indexes, in the order they are started.</param>
    /// <param name="harvest">Reads a scratch's work counts once, after its worker is done.</param>
    /// <param name="rangeSize">Items per range handed to a worker, or <see cref="AutomaticRangeSize"/>.</param>
    public static (TOut[] Results, TWork Work) Run<TScratch, TOut, TWork>(
        Execution execution,
        ImmutableArray<int> order,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest,
        int rangeSize)
        where TWork : IWork<TWork>
    {
        Permutation.Require(order, nameof(order));
        return RunSummingWork(execution, order.Length, position => order[position], newScratch, body, harvest, rangeSize);
    }

    /// <summary>Runs every item, in the work order; throws unless the order covers exactly <paramref name="itemCount"/> items.</summary>
    public static (TOut[] Results, TWork Work) Run<TScratch, TOut, TWork>(
        Execution execution,
        WorkOrder order,
        int itemCount,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest,
        int rangeSize)
        where TWork : IWork<TWork>
    {
        order.RequireCovers(itemCount);
        return Run(execution, order.ItemsInOrder, newScratch, body, harvest, rangeSize);
    }

    /// <summary>Runs every item, in the work order; throws unless the order covers exactly <paramref name="itemCount"/> items.</summary>
    public static TOut[] Run<TScratch, TOut>(
        Execution execution, WorkOrder order, int itemCount, Func<TScratch> newScratch, Func<int, TScratch, TOut> body, int rangeSize)
    {
        order.RequireCovers(itemCount);
        return Run(execution, order.ItemsInOrder, newScratch, body, rangeSize);
    }

    /// <summary>Starts the items in index order.</summary>
    public static (TOut[] Results, TWork Work) Run<TScratch, TOut, TWork>(
        Execution execution,
        int count,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest,
        int rangeSize)
        where TWork : IWork<TWork> =>
        RunSummingWork(execution, count, static position => position, newScratch, body, harvest, rangeSize);

    /// <param name="order">A permutation of the item indexes, in the order they are started.</param>
    public static TOut[] Run<TScratch, TOut>(
        Execution execution, ImmutableArray<int> order, Func<TScratch> newScratch, Func<int, TScratch, TOut> body, int rangeSize)
    {
        Permutation.Require(order, nameof(order));
        return Map(execution, order.Length, position => order[position], newScratch, body, rangeSize, finish: static _ => { });
    }

    public static TOut[] Run<TScratch, TOut>(
        Execution execution, int count, Func<TScratch> newScratch, Func<int, TScratch, TOut> body, int rangeSize) =>
        Map(execution, count, static position => position, newScratch, body, rangeSize, finish: static _ => { });

    public static TOut[] Run<TOut>(Execution execution, int count, Func<int, TOut> body, int rangeSize) =>
        Map<object?, TOut>(execution, count, static position => position, static () => null, (item, _) => body(item), rangeSize, finish: static _ => { });

    /// <summary>Runs <paramref name="body"/> for the given subset of item indexes, started in the work order's rank.</summary>
    /// <returns>Result p is body(indices[p], scratch), whatever the order or the number of workers.</returns>
    public static TOut[] RunOver<TScratch, TOut>(
        Execution execution,
        WorkOrder order,
        IReadOnlyList<int> indices,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        int rangeSize) =>
        Run(execution, order.Among(indices), newScratch, (position, scratch) => body(indices[position], scratch), rangeSize);

    /// <summary>
    /// Finds each group's first matching member. A group's members are tested one after another in
    /// the group's own order and the rest are skipped once one matches, so first means first in that
    /// order, never first to finish; whole groups are the units of parallel work.
    /// </summary>
    /// <param name="groups">Item indexes per group, in each group's order.</param>
    /// <returns>The matching items' indexes ascending, and the summed work.</returns>
    public static (List<int> Matches, TWork Work) FirstMatchPerGroup<TScratch, TWork>(
        Execution execution,
        IReadOnlyList<IReadOnlyList<int>> groups,
        Func<TScratch> newScratch,
        Func<int, TScratch, bool> isMatch,
        Func<TScratch, TWork> harvest,
        int rangeSize)
        where TWork : IWork<TWork>
    {
        var (firstMatches, work) = Run(
            execution, groups.Count, newScratch, (group, scratch) => FirstMatchIn(groups[group], isMatch, scratch), harvest, rangeSize);
        var matches = ParallelResults.Compact(firstMatches);
        matches.Sort();
        return (matches, work);
    }

    private static int? FirstMatchIn<TScratch>(IReadOnlyList<int> group, Func<int, TScratch, bool> isMatch, TScratch scratch)
    {
        foreach (var item in group)
        {
            if (isMatch(item, scratch)) return item;
        }
        return null;
    }

    private static (TOut[] Results, TWork Work) RunSummingWork<TScratch, TOut, TWork>(
        Execution execution,
        int count,
        Func<int, int> itemAt,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        Func<TScratch, TWork> harvest,
        int rangeSize)
        where TWork : IWork<TWork>
    {
        var work = TWork.Zero;
        var workLock = new object();
        var results = Map(execution, count, itemAt, newScratch, body, rangeSize, scratch =>
        {
            var harvested = harvest(scratch);
            lock (workLock) work += harvested;
        });
        return (results, work);
    }

    private static TOut[] Map<TScratch, TOut>(
        Execution execution,
        int count,
        Func<int, int> itemAt,
        Func<TScratch> newScratch,
        Func<int, TScratch, TOut> body,
        int rangeSize,
        Action<TScratch> finish)
    {
        var results = new TOut[count];
        if (count == 0) return results;
        var failures = new ConcurrentDictionary<int, Exception>();
        Parallel.ForEach(
            Partitioner.Create(0, count, ChooseRangeSize(execution, count, rangeSize)),
            new ParallelOptions { MaxDegreeOfParallelism = execution.Workers },
            newScratch,
            (range, _, scratch) =>
            {
                for (var position = range.Item1; position < range.Item2; position++)
                {
                    var item = itemAt(position);
                    try
                    {
                        results[item] = body(item, scratch);
                    }
                    catch (Exception ex)
                    {
                        failures.TryAdd(item, ex);
                    }
                }
                return scratch;
            },
            finish);
        ThrowFailureOfLowestItem(failures);
        return results;
    }

    /// <remarks>
    /// Every item runs even after another failed, so that the reported failure is the lowest item's and never
    /// depends on which worker failed first. It is wrapped like the failures of a parallel loop are; an
    /// <see cref="OutOfMemoryException"/> is reported before any other failure.
    /// </remarks>
    private static void ThrowFailureOfLowestItem(ConcurrentDictionary<int, Exception> failures)
    {
        if (failures.IsEmpty) return;
        var inItemOrder = failures.OrderBy(failure => failure.Key).Select(failure => failure.Value).ToList();
        throw new AggregateException(inItemOrder.FirstOrDefault(failure => failure is OutOfMemoryException) ?? inItemOrder[0]);
    }

    private static int ChooseRangeSize(Execution execution, int count, int rangeSize)
    {
        if (rangeSize != AutomaticRangeSize) return rangeSize;
        return Math.Max(1, count / (execution.Workers * RangesPerWorker));
    }
}
