using System.Collections.Concurrent;

namespace AddedObjectRemover.Tests.Memo;

public class MemoTests
{
    private const int Threads = 8;
    private const int Calls = 64;

    private static readonly TimeSpan OverlapTimeout = TimeSpan.FromSeconds(10);
    private static readonly ParallelOptions Parallel8 = new() { MaxDegreeOfParallelism = Threads };

    private sealed record Blob(long Size);

    /// <summary>Runs the body on dedicated threads that all exist before any starts, so the overlap does not depend on the thread pool.</summary>
    private static void RunTogether(int count, Action<int, IReadOnlyList<Thread>> body)
    {
        var failures = new ConcurrentQueue<Exception>();
        var threads = new List<Thread>();
        for (var i = 0; i < count; i++)
        {
            var index = i;
            threads.Add(new Thread(() =>
            {
                try
                {
                    body(index, threads);
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }
            }));
        }
        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());
        if (!failures.IsEmpty) throw new AggregateException(failures);
    }

    /// <summary>Blocks the calling build until every other thread waits on a lock, i.e. is a waiter of this build.</summary>
    private static void WaitUntilOtherThreadsAreBlocked(IReadOnlyList<Thread> threads)
    {
        var others = threads.Where(thread => thread != Thread.CurrentThread).ToList();
        Assert.True(
            SpinWait.SpinUntil(() => others.All(thread => thread.ThreadState.HasFlag(ThreadState.WaitSleepJoin)), OverlapTimeout),
            "the other threads never started waiting for the build");
    }

    [Fact]
    public void BuiltOncePerKeyComputesEachKeyOnceWhileOtherThreadsWait()
    {
        var computations = 0;
        var memo = new ComputedOncePerKey<string, Blob>(Publication.BuiltOnce, StringComparer.Ordinal);
        var results = new Blob[Threads];

        RunTogether(Threads, (i, threads) =>
        {
            results[i] = memo.Get("key", () =>
            {
                Interlocked.Increment(ref computations);
                WaitUntilOtherThreadsAreBlocked(threads);
                return new Blob(1);
            });
        });

        Assert.Equal(1, computations);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public void FirstWriteWinsMayComputeSeveralTimesButPublishesOneValue()
    {
        var computations = 0;
        var memo = new ComputedOncePerKey<string, Blob>(Publication.FirstWriteWins, StringComparer.Ordinal);
        using var allComputing = new Barrier(Threads);
        var results = new Blob[Threads];

        RunTogether(Threads, (i, _) =>
        {
            results[i] = memo.Get("key", () =>
            {
                Interlocked.Increment(ref computations);
                Assert.True(allComputing.SignalAndWait(OverlapTimeout), "the computations never overlapped");
                return new Blob(i);
            });
        });

        Assert.Equal(Threads, computations);
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Single(memo.Contents());
    }

    [Fact]
    public void ContentsHoldsOnlyValuesComputedSoFar()
    {
        var memo = new ComputedOncePerKey<string, int>(Publication.BuiltOnce, StringComparer.OrdinalIgnoreCase);
        memo.Get("a", () => 1);
        memo.Get("A", () => 2);
        memo.Get("b", () => 3);

        Assert.Equal([1, 3], memo.Contents().Order());
    }

    [Fact]
    public void FirstWriteWinsComputesAgainAfterAFailure()
    {
        var calls = 0;
        var memo = new ComputedOncePerKey<string, int>(Publication.FirstWriteWins, StringComparer.Ordinal);
        int Compute() => ++calls == 1 ? throw new IOException("unreadable") : 7;

        Assert.Throws<IOException>(() => memo.Get("key", Compute));
        Assert.Equal(7, memo.Get("key", Compute));
    }

    [Fact]
    public void BuiltOnceGivesTheFailureToEveryWaiterAndToLaterCallsWithoutComputingAgain()
    {
        var computations = 0;
        var memo = new ComputedOncePerKey<string, int>(Publication.BuiltOnce, StringComparer.Ordinal);
        var failures = new Exception?[Threads];
        int Compute()
        {
            Interlocked.Increment(ref computations);
            throw new IOException("unreadable");
        }

        RunTogether(Threads, (i, threads) =>
        {
            failures[i] = Record.Exception(() => memo.Get("key", () =>
            {
                Interlocked.Increment(ref computations);
                WaitUntilOtherThreadsAreBlocked(threads);
                throw new IOException("unreadable");
            }));
        });

        Assert.All(failures, failure => Assert.IsType<IOException>(failure));
        Assert.Throws<IOException>(() => memo.Get("key", Compute));
        Assert.Equal(1, computations);
        Assert.Empty(memo.Contents());
    }

    [Fact]
    public void ComputedOnceComputesOnceWhenAskedByManyThreads()
    {
        var computations = 0;
        IReadOnlyList<Thread>? askingThreads = null;
        var once = new ComputedOnce<Blob>(() =>
        {
            Interlocked.Increment(ref computations);
            WaitUntilOtherThreadsAreBlocked(askingThreads!);
            return new Blob(1);
        });
        var results = new Blob[Threads];

        RunTogether(Threads, (i, threads) =>
        {
            askingThreads = threads;
            results[i] = once.Value;
        });

        Assert.Equal(1, computations);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public void IndexMemoComputesEachPositionOnceWhenAskedInTurn()
    {
        var computed = new List<int>();
        var memo = new IndexMemo<int>(3);
        int Compute(int index)
        {
            computed.Add(index);
            return index * 10;
        }

        Assert.Equal(20, memo.GetOrCompute(2, Compute));
        Assert.Equal(20, memo.GetOrCompute(2, Compute));
        Assert.Equal(0, memo.GetOrCompute(0, Compute));
        Assert.Equal([2, 0], computed);
    }

    [Fact]
    public void IndexMemoGivesTheSameValueToConcurrentCallers()
    {
        var memo = new IndexMemo<(int, int)>(Calls);
        var seen = new (int, int)[Calls * Threads];

        Parallel.For(0, Calls * Threads, Parallel8, i =>
        {
            var index = i % Calls;
            seen[i] = memo.GetOrCompute(index, position => (position, position * 2));
        });

        for (var i = 0; i < seen.Length; i++) Assert.Equal((i % Calls, i % Calls * 2), seen[i]);
    }

    private const long BlobSize = 100;
    private const long LimitSize = 250;
    private const long EvictDownTo = 200;

    private static EvictingLeasedStore<string, Blob> NewStore(Dictionary<string, int> builds, Func<string, Blob?>? build = null) =>
        new(
            key =>
            {
                lock (builds) builds[key] = builds.GetValueOrDefault(key) + 1;
                return build is null ? new Blob(BlobSize) : build(key);
            },
            blob => blob.Size,
            LimitSize,
            EvictDownTo,
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void StoreBuildsEachKeyOnceUnderConcurrency()
    {
        var builds = new Dictionary<string, int>();
        IReadOnlyList<Thread>? askingThreads = null;
        var store = NewStore(builds, _ =>
        {
            WaitUntilOtherThreadsAreBlocked(askingThreads!);
            return new Blob(1);
        });
        var values = new Blob?[Threads];

        RunTogether(Threads, (i, threads) =>
        {
            askingThreads = threads;
            using var lease = store.Acquire("a");
            values[i] = lease.Value;
        });

        Assert.Equal(1, builds["a"]);
        Assert.NotNull(values[0]);
        Assert.All(values, value => Assert.Same(values[0], value));
    }

    [Fact]
    public void StoreGivesAFailedBuildToEveryWaiterThenResetsTheSlotAndBuildsAgain()
    {
        var builds = new Dictionary<string, int>();
        IReadOnlyList<Thread>? askingThreads = null;
        var store = NewStore(builds, _ =>
        {
            int attempt;
            lock (builds) attempt = builds["flaky"];
            if (attempt > 1) return new Blob(BlobSize);
            WaitUntilOtherThreadsAreBlocked(askingThreads!);
            throw new IOException("unreadable");
        });
        var failures = new Exception?[Threads];

        RunTogether(Threads, (i, threads) =>
        {
            askingThreads = threads;
            failures[i] = Record.Exception(() => store.Acquire("flaky"));
        });

        Assert.All(failures, failure => Assert.IsType<IOException>(failure));
        Assert.Equal(1, builds["flaky"]);
        using (var rebuilt = store.Acquire("flaky")) Assert.NotNull(rebuilt.Value);
        using (var reused = store.Acquire("flaky")) Assert.NotNull(reused.Value);
        Assert.Equal(2, builds["flaky"]);
    }

    [Fact]
    public void StoreNeverEvictsALeasedValueWhileOtherThreadsForceEvictions()
    {
        const int OtherKeys = 32;
        var builds = new Dictionary<string, int>();
        var store = NewStore(builds);
        using var held = store.Acquire("held");

        RunTogether(Threads, (_, _) =>
        {
            for (var pass = 0; pass < 2; pass++)
            {
                for (var key = 0; key < OtherKeys; key++)
                {
                    using var lease = store.Acquire($"other{key}");
                    Assert.NotNull(lease.Value);
                    using var heldAgain = store.Acquire("held");
                    Assert.Same(held.Value, heldAgain.Value);
                }
            }
        });

        Assert.Equal(1, builds["held"]);
        Assert.True(builds.Where(entry => entry.Key != "held").Sum(entry => entry.Value) > OtherKeys, "idle values were never evicted");
    }

    [Fact]
    public void StoreEvictsLeastRecentlyUsedIdleValuesAndKeepsLeasedOnes()
    {
        var builds = new Dictionary<string, int>();
        var store = NewStore(builds);
        using var leasedA = store.Acquire("a");
        store.Acquire("b").Dispose();
        using var leasedC = store.Acquire("c");

        using var againA = store.Acquire("a");
        Assert.Same(leasedA.Value, againA.Value);
        Assert.Equal(1, builds["a"]);

        store.Acquire("b").Dispose();
        Assert.Equal(2, builds["b"]);
    }

    [Fact]
    public void StoreBuildsAnEvictedKeyAgainOnItsNextUse()
    {
        var builds = new Dictionary<string, int>();
        var store = NewStore(builds);
        store.Acquire("a").Dispose();
        store.Acquire("b").Dispose();
        using var leasedC = store.Acquire("c");

        store.Acquire("a").Dispose();

        Assert.Equal(2, builds["a"]);
    }

    [Fact]
    public void StoreRetriesAfterAFailedBuild()
    {
        var builds = new Dictionary<string, int>();
        var store = NewStore(builds, _ => builds["flaky"] <= 2 ? throw new IOException("unreadable") : new Blob(BlobSize));

        Assert.Throws<IOException>(() => store.Acquire("flaky"));
        Assert.Throws<IOException>(() => store.Acquire("flaky"));
        using var lease = store.Acquire("flaky");

        Assert.NotNull(lease.Value);
        Assert.Equal(3, builds["flaky"]);
    }

    [Fact]
    public void StoreKeepsAKeyWithoutValueAsNothingAndDoesNotBuildItAgain()
    {
        var builds = new Dictionary<string, int>();
        var store = NewStore(builds, _ => null);

        using (var first = store.Acquire("none")) Assert.Null(first.Value);
        using (var second = store.Acquire("none")) Assert.Null(second.Value);

        Assert.Equal(1, builds["none"]);
    }

    [Fact]
    public void DisposingADefaultLeaseDoesNothing()
    {
        Assert.Null(Record.Exception(() => default(Lease<Blob>).Dispose()));
    }
}
