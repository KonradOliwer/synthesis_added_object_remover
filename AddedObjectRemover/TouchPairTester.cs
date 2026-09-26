using System.Collections.Concurrent;
using System.Diagnostics;

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
    string?[] meshPaths,
    BaseObjectShapeProvider shapes,
    float tolerance)
{
    private const int PairsPerChunk = 64;

    private readonly TriangleTreeCache _cache = new(shapes.ReadGeometry);
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

    /// <summary>Minimum surface distance of a pair known to touch, world units; NaN when either mesh has no usable triangles.</summary>
    public float MeasureMinSurfaceDistance(TargetPair pair, TouchScratch scratch)
    {
        using var first = _cache.Acquire(MeshPathOf(pair.First));
        using var second = _cache.Acquire(MeshPathOf(pair.Second));
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
        using var first = _cache.Acquire(MeshPathOf(pair.First));
        using var second = _cache.Acquire(MeshPathOf(pair.Second));
        if (first.Tree == null || second.Tree == null) return PairTouch.NoGeometry;
        var touches = MeshTouchTest.Touches(
            first.Tree, targets[pair.First].Transform, second.Tree, targets[pair.Second].Transform, tolerance, scratch);
        return touches ? PairTouch.Touching : PairTouch.Apart;
    }

    /// <remarks>Targets without a mesh path never take part in a candidate pair.</remarks>
    private string MeshPathOf(int target) =>
        meshPaths[target] ?? throw new UnreachableException($"Target {target} has no mesh but was paired for a touch test.");

    private void CountResults(PairTouch[] results)
    {
        _pairsTested += results.Length;
        _touchingPairs += results.Count(r => r == PairTouch.Touching);
        _pairsWithoutGeometry += results.Count(r => r == PairTouch.NoGeometry);
    }
}
