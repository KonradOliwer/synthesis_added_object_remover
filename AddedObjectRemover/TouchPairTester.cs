using System.Collections.Concurrent;
using System.Numerics;

namespace AddedObjectRemover;

internal enum PairTouch : byte { NoGeometry, Apart, Touching }

/// <summary>Two target indices.</summary>
internal readonly record struct TargetPair(int First, int Second);

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

    /// <summary>
    /// Moves the enclosure test point slightly off the second mesh's centre, per axis as a
    /// fraction of its size: an enclosing mesh placed concentrically and symmetric about that
    /// centre has its triangle diagonals there, where line crossings are not counted.
    /// </summary>
    private static readonly Vector3 CentreNudgeFractions = new(0.0137f, 0.0219f, 0.0071f);

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

    /// <summary>Per pair: whether the first's mesh encloses a point near the centre of the second's mesh (<see cref="PointContactTest.IsEnclosed"/>).</summary>
    public bool[] TestEnclosures(IReadOnlyList<TargetPair> pairs, ParallelOptions parallelOptions)
    {
        var results = new bool[pairs.Count];
        if (pairs.Count == 0) return results;

        Parallel.ForEach(
            Partitioner.Create(0, pairs.Count, PairsPerChunk),
            parallelOptions,
            () => new List<int>(),
            (range, _, scratch) =>
            {
                for (var i = range.Item1; i < range.Item2; i++) results[i] = EnclosesNearCentreOfSecond(pairs[i], scratch);
                return scratch;
            },
            _ => { });
        return results;
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

    private bool EnclosesNearCentreOfSecond(TargetPair pair, List<int> scratch)
    {
        using var first = _cache.Acquire(meshPaths.Get(pair.First));
        using var second = _cache.Acquire(meshPaths.Get(pair.Second));
        if (first.Tree is not { } enclosing || second.Tree is not { } enclosed) return false;

        var toFirst = RelativeTransform.Create(from: targets[pair.Second].Transform, to: targets[pair.First].Transform);
        var nearCentre = enclosed.Bounds.Center + enclosed.Bounds.Size * CentreNudgeFractions;
        return PointContactTest.IsEnclosed(enclosing, toFirst.Apply(nearCentre), scratch);
    }

    private void CountResults(PairTouch[] results)
    {
        _pairsTested += results.Length;
        _touchingPairs += results.Count(r => r == PairTouch.Touching);
        _pairsWithoutGeometry += results.Count(r => r == PairTouch.NoGeometry);
    }
}
