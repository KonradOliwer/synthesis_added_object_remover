using System.Collections.Concurrent;

namespace AddedObjectRemover;

internal enum PairTouch : byte { Untested, NoGeometry, Apart, Touching }

internal sealed record NarrowPhaseResult(
    PairTouch[] Results,
    int PairsTested,
    long TrianglePairsTested,
    TriangleTreeStats Meshes);

/// <summary>
/// Narrow phase of the touch search: runs <see cref="MeshTouchTest"/> on every needed candidate
/// pair, in parallel. Pairs are handed out in their spatially local (First, Second) order, and
/// the mesh cache is told up front how often each mesh will be used, so each mesh is built once
/// and dropped right after its last pair. Results are stored per pair, so they do not depend on
/// scheduling.
/// </summary>
internal sealed class TouchPairTester
{
    private const int PairsPerChunk = 64;

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IReadOnlyList<CandidatePair> _pairs;
    private readonly string?[] _meshPaths;
    private readonly float _tolerance;
    private readonly TriangleTreeCache _cache;

    private TouchPairTester(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<CandidatePair> pairs,
        string?[] meshPaths,
        IReadOnlyList<int> pairsToTest,
        BaseObjectShapeProvider shapes,
        float tolerance)
    {
        _targets = targets;
        _pairs = pairs;
        _meshPaths = meshPaths;
        _tolerance = tolerance;
        _cache = new TriangleTreeCache(shapes.ReadGeometry, CountMeshUses(pairsToTest));
    }

    public static NarrowPhaseResult TestPairs(
        IReadOnlyList<TargetObject> targets,
        TouchCandidates candidates,
        bool[] needed,
        BaseObjectShapeProvider shapes,
        float tolerance,
        ParallelOptions parallelOptions)
    {
        var meshPaths = targets.Select(t => shapes.GetMeshPath(t.Base)).ToArray();
        var pairsToTest = Enumerable.Range(0, needed.Length).Where(k => needed[k]).ToList();
        var tester = new TouchPairTester(targets, candidates.Pairs, meshPaths, pairsToTest, shapes, tolerance);
        return tester.Run(pairsToTest, parallelOptions);
    }

    /// <summary>Uses per mesh path: one per pair end, for pairs where both ends have a mesh.</summary>
    private Dictionary<string, int> CountMeshUses(IReadOnlyList<int> pairsToTest)
    {
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in pairsToTest)
        {
            if (_meshPaths[_pairs[k].First] is not { } firstPath || _meshPaths[_pairs[k].Second] is not { } secondPath) continue;
            uses[firstPath] = uses.GetValueOrDefault(firstPath) + 1;
            uses[secondPath] = uses.GetValueOrDefault(secondPath) + 1;
        }
        return uses;
    }

    private NarrowPhaseResult Run(IReadOnlyList<int> pairsToTest, ParallelOptions parallelOptions)
    {
        var results = new PairTouch[_pairs.Count];
        long trianglePairs = 0;
        if (pairsToTest.Count == 0) return new NarrowPhaseResult(results, 0, 0, _cache.GetStats());

        Parallel.ForEach(
            Partitioner.Create(0, pairsToTest.Count, PairsPerChunk),
            parallelOptions,
            () => new TouchScratch(),
            (range, _, scratch) =>
            {
                for (var i = range.Item1; i < range.Item2; i++) results[pairsToTest[i]] = TestPair(pairsToTest[i], scratch);
                return scratch;
            },
            scratch => Interlocked.Add(ref trianglePairs, scratch.TrianglePairsTested));
        return new NarrowPhaseResult(results, pairsToTest.Count, trianglePairs, _cache.GetStats());
    }

    private PairTouch TestPair(int k, TouchScratch scratch)
    {
        var (first, second) = _pairs[k];
        if (_meshPaths[first] is not { } firstPath || _meshPaths[second] is not { } secondPath) return PairTouch.NoGeometry;
        try
        {
            return TestMeshes(first, firstPath, second, secondPath, scratch);
        }
        finally
        {
            _cache.Release(firstPath);
            _cache.Release(secondPath);
        }
    }

    private PairTouch TestMeshes(int first, string firstPath, int second, string secondPath, TouchScratch scratch)
    {
        if (_cache.Acquire(firstPath) is not { } firstTree || _cache.Acquire(secondPath) is not { } secondTree)
        {
            return PairTouch.NoGeometry;
        }
        var touches = MeshTouchTest.Touches(
            firstTree, _targets[first].Transform, secondTree, _targets[second].Transform, _tolerance, scratch);
        return touches ? PairTouch.Touching : PairTouch.Apart;
    }
}
