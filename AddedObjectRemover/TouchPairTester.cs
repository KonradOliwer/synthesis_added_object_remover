using System.Collections.Concurrent;

namespace AddedObjectRemover;

internal enum PairTouch : byte { NoGeometry, Apart, Touching }

/// <summary>When two meshes are in contact.</summary>
internal enum ContactRule
{
    /// <summary>Their surfaces come within the tolerance.</summary>
    Touch,

    /// <summary>
    /// Their surfaces come within the tolerance, or the first's mesh encloses the centre of the
    /// second's mesh bounds (<see cref="PointContactTest.IsEnclosed"/>).
    /// </summary>
    TouchOrEnclose,
}

internal readonly record struct TargetPair(int First, int Second);

/// <summary>One thread's buffers and counts for <see cref="TouchPairTester.FindFirstInContact"/>.</summary>
internal sealed class ContactScratch
{
    public TouchScratch Touch { get; } = new();
    public List<int> Enclosure { get; } = [];
    public int PairsTested { get; private set; }
    public int TouchingPairs { get; private set; }
    public int PairsWithoutGeometry { get; private set; }

    public void Count(PairTouch touch)
    {
        PairsTested++;
        if (touch == PairTouch.Touching) TouchingPairs++;
        if (touch == PairTouch.NoGeometry) PairsWithoutGeometry++;
    }
}

internal readonly record struct PairTestStats(
    int PairsTested,
    int TouchingPairs,
    int PairsWithoutGeometry,
    long TrianglePairsTested,
    TriangleTreeStats Meshes);

/// <summary>
/// Narrow phase of the touch search: runs <see cref="MeshTouchTest"/> on target pairs in parallel,
/// with each mesh's triangle tree taken from one cache shared by all calls. Results are stored per
/// pair, so they do not depend on scheduling.
/// </summary>
internal sealed class TouchPairTester(
    IReadOnlyList<TargetObject> targets,
    TargetMeshPaths meshPaths,
    TriangleTreeCache cache,
    float tolerance)
{
    private const int PairsPerChunk = 64;

    private readonly TriangleTreeCache _cache = cache;
    private int _pairsTested;
    private int _touchingPairs;
    private int _pairsWithoutGeometry;
    private long _trianglePairsTested;

    public PairTouch[] TestPairs(IReadOnlyList<TargetPair> pairs, ParallelOptions parallelOptions)
    {
        var results = new PairTouch[pairs.Count];
        if (pairs.Count == 0) return results;

        Parallel.ForEach(
            Partitioner.Create(0, pairs.Count, PairsPerChunk),
            parallelOptions,
            () => new TouchScratch(),
            (range, _, scratch) =>
            {
                for (var i = range.Item1; i < range.Item2; i++) results[i] = TestPair(pairs[i], scratch);
                return scratch;
            },
            scratch => Interlocked.Add(ref _trianglePairsTested, scratch.TrianglePairsTested));
        CountResults(results);
        return results;
    }

    /// <summary>
    /// For each distinct second member, the first of its pairs, in the given order, whose meshes are
    /// in contact: its pairs are tested in that order and the rest are skipped once one is. The found
    /// pairs are returned in the given order.
    /// </summary>
    public List<TargetPair> FindFirstInContact(IReadOnlyList<TargetPair> pairs, ContactRule rule, ParallelOptions parallelOptions)
    {
        var pairsBySecond = GroupBySecond(pairs);
        var firstInContact = new int[pairsBySecond.Count];
        Parallel.For(
            0,
            pairsBySecond.Count,
            parallelOptions,
            () => new ContactScratch(),
            (group, _, scratch) =>
            {
                firstInContact[group] = FindFirstInContact(pairs, pairsBySecond[group], rule, scratch);
                return scratch;
            },
            AddCounts);
        return firstInContact.Where(pairIndex => pairIndex >= 0).Order().Select(pairIndex => pairs[pairIndex]).ToList();
    }

    /// <returns>Pair indices per distinct second member, each list in the given order.</returns>
    private static List<List<int>> GroupBySecond(IReadOnlyList<TargetPair> pairs)
    {
        var groups = new List<List<int>>();
        var groupOf = new Dictionary<int, int>();
        for (var k = 0; k < pairs.Count; k++)
        {
            if (!groupOf.TryGetValue(pairs[k].Second, out var group))
            {
                group = groups.Count;
                groupOf[pairs[k].Second] = group;
                groups.Add([]);
            }
            groups[group].Add(k);
        }
        return groups;
    }

    /// <returns>The index of the first pair in contact; -1 when none is.</returns>
    private int FindFirstInContact(IReadOnlyList<TargetPair> pairs, List<int> pairIndices, ContactRule rule, ContactScratch scratch)
    {
        foreach (var pairIndex in pairIndices)
        {
            var touch = TestPair(pairs[pairIndex], scratch.Touch);
            scratch.Count(touch);
            if (touch == PairTouch.Touching) return pairIndex;
            if (touch == PairTouch.Apart && rule == ContactRule.TouchOrEnclose && EnclosesCentreOfSecond(pairs[pairIndex], scratch.Enclosure))
            {
                return pairIndex;
            }
        }
        return -1;
    }

    private void AddCounts(ContactScratch scratch)
    {
        Interlocked.Add(ref _pairsTested, scratch.PairsTested);
        Interlocked.Add(ref _touchingPairs, scratch.TouchingPairs);
        Interlocked.Add(ref _pairsWithoutGeometry, scratch.PairsWithoutGeometry);
        Interlocked.Add(ref _trianglePairsTested, scratch.Touch.TrianglePairsTested);
    }

    /// <summary>Minimum surface distance of a pair known to touch, world units; NaN when either mesh has no usable triangles.</summary>
    public float MeasureMinSurfaceDistance(TargetPair pair, TouchScratch scratch)
    {
        using var first = _cache.Acquire(meshPaths.Get(pair.First));
        using var second = _cache.Acquire(meshPaths.Get(pair.Second));
        if (first.Tree == null || second.Tree == null) return float.NaN;
        return MeshTouchTest.MinSurfaceDistance(
            first.Tree, targets[pair.First].Transform, second.Tree, targets[pair.Second].Transform, tolerance, scratch);
    }

    public PairTestStats GetStats() => new(
        _pairsTested,
        _touchingPairs,
        _pairsWithoutGeometry,
        Interlocked.Read(ref _trianglePairsTested),
        _cache.GetStats());

    private PairTouch TestPair(TargetPair pair, TouchScratch scratch)
    {
        using var first = _cache.Acquire(meshPaths.Get(pair.First));
        using var second = _cache.Acquire(meshPaths.Get(pair.Second));
        if (first.Tree == null || second.Tree == null) return PairTouch.NoGeometry;
        var touches = MeshTouchTest.Touches(
            first.Tree, targets[pair.First].Transform, second.Tree, targets[pair.Second].Transform, tolerance, scratch);
        return touches ? PairTouch.Touching : PairTouch.Apart;
    }

    private bool EnclosesCentreOfSecond(TargetPair pair, List<int> scratch)
    {
        using var first = _cache.Acquire(meshPaths.Get(pair.First));
        using var second = _cache.Acquire(meshPaths.Get(pair.Second));
        if (first.Tree is not { } enclosing || second.Tree is not { } enclosed) return false;

        var toFirst = RelativeTransform.Create(from: targets[pair.Second].Transform, to: targets[pair.First].Transform);
        return PointContactTest.IsEnclosed(enclosing, toFirst.Apply(enclosed.Bounds.Center), scratch);
    }

    private void CountResults(PairTouch[] results)
    {
        _pairsTested += results.Length;
        _touchingPairs += results.Count(r => r == PairTouch.Touching);
        _pairsWithoutGeometry += results.Count(r => r == PairTouch.NoGeometry);
    }
}
