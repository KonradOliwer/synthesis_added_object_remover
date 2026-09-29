using System.Collections.Immutable;

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

/// <summary>One worker's buffers and counts for the pair tests of <see cref="TouchPairTester"/>.</summary>
internal sealed class ContactScratch
{
    private int _pairsTested;
    private int _touchingPairs;
    private int _pairsWithoutGeometry;

    public TouchScratch Touch { get; } = new();
    public List<int> Enclosure { get; } = [];

    public PairTouch Count(PairTouch touch)
    {
        _pairsTested++;
        if (touch == PairTouch.Touching) _touchingPairs++;
        if (touch == PairTouch.NoGeometry) _pairsWithoutGeometry++;
        return touch;
    }

    public PairTestStats Harvest() => new(_pairsTested, _touchingPairs, _pairsWithoutGeometry, Touch.TrianglePairsTested);
}

internal readonly record struct PairTestStats(
    int PairsTested,
    int TouchingPairs,
    int PairsWithoutGeometry,
    long TrianglePairsTested) : IWork<PairTestStats>
{
    public static PairTestStats Zero => default;

    public static PairTestStats operator +(PairTestStats first, PairTestStats second) => new(
        first.PairsTested + second.PairsTested,
        first.TouchingPairs + second.TouchingPairs,
        first.PairsWithoutGeometry + second.PairsWithoutGeometry,
        first.TrianglePairsTested + second.TrianglePairsTested);

    public static PairTestStats Sum(IEnumerable<PairTestStats> works) => works.Aggregate(Zero, (total, work) => total + work);
}

/// <summary>
/// Narrow phase of the touch search: runs <see cref="MeshTouchTest"/> on target pairs in parallel,
/// with each mesh's triangle tree taken from one cache shared by all calls. Results are stored per
/// pair and each call returns its own work counts, so neither depends on scheduling.
/// </summary>
internal sealed class TouchPairTester(
    IReadOnlyList<TargetObject> targets,
    TargetMeshPaths meshPaths,
    TriangleStore cache,
    float tolerance)
{
    private const int PairsPerRange = 64;

    /// <summary>A group stops at its first pair in contact, so groups differ too much in cost to hand out several at once.</summary>
    private const int GroupsPerRange = ParallelMap.OneItemPerRange;

    private const int NoPairInContact = -1;

    private readonly TriangleStore _cache = cache;

    public (PairTouch[] Results, PairTestStats Work) TestPairs(IReadOnlyList<TargetPair> pairs, Execution execution) =>
        ParallelMap.Run(
            execution,
            pairs.Count,
            () => new ContactScratch(),
            (k, scratch) => scratch.Count(TestPair(pairs[k], scratch.Touch)),
            scratch => scratch.Harvest(),
            PairsPerRange);

    /// <summary>
    /// For each distinct second member, the first of its pairs, in the given order, whose meshes are
    /// in contact: its pairs are tested in that order and the rest are skipped once one is. The found
    /// pairs are returned in the given order.
    /// </summary>
    public (List<TargetPair> Found, PairTestStats Work) FindFirstInContact(IReadOnlyList<TargetPair> pairs, ContactRule rule, Execution execution)
    {
        var pairsBySecond = GroupBySecond(pairs);
        var (firstInContact, work) = ParallelMap.Run(
            execution,
            pairsBySecond.Count,
            () => new ContactScratch(),
            (group, scratch) => FindFirstInContact(pairs, pairsBySecond[group], rule, scratch),
            scratch => scratch.Harvest(),
            GroupsPerRange);
        var found = firstInContact.Where(pairIndex => pairIndex != NoPairInContact).Order().Select(pairIndex => pairs[pairIndex]).ToList();
        return (found, work);
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

    /// <returns>The index of the first pair in contact; <see cref="NoPairInContact"/> when none is.</returns>
    private int FindFirstInContact(IReadOnlyList<TargetPair> pairs, List<int> pairIndices, ContactRule rule, ContactScratch scratch)
    {
        foreach (var pairIndex in pairIndices)
        {
            var touch = scratch.Count(TestPair(pairs[pairIndex], scratch.Touch));
            if (touch == PairTouch.Touching) return pairIndex;
            if (touch == PairTouch.Apart && rule == ContactRule.TouchOrEnclose && EnclosesCentreOfSecond(pairs[pairIndex], scratch.Enclosure))
            {
                return pairIndex;
            }
        }
        return NoPairInContact;
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
}
