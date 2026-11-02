namespace AddedObjectRemover;

public enum PairTouch : byte { NoGeometry, Apart, Touching }

public readonly record struct IndexPair(int First, int Second);

/// <summary>One worker's buffers and counts for the pair tests of <see cref="MeshPairTester"/>.</summary>
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

public readonly record struct PairTestStats(
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
}

/// <summary>
/// Tests pairs of placed meshes in parallel, with each mesh's triangle tree taken from one cache
/// shared by all calls. Results are stored per pair and each call returns its own work counts, so
/// neither depends on scheduling. Pair members are indices; the caller says each one's mesh path
/// and placement.
/// </summary>
/// <param name="pathOf">The mesh path of an index.</param>
/// <param name="transformOf">The placement of an index.</param>
public sealed class MeshPairTester(
    ITriangleMeshes meshes,
    Func<int, string> pathOf,
    Func<int, PlacedTransform> transformOf,
    float tolerance)
{
    private const int PairsPerRange = 64;

    /// <summary>A group stops at its first pair in contact, so groups differ too much in cost to hand out several at once.</summary>
    private const int GroupsPerRange = ParallelMap.OneItemPerRange;

    public (PairTouch[] Results, PairTestStats Work) TestPairs(IReadOnlyList<IndexPair> pairs, Execution execution) =>
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
    /// <param name="alsoWhenCentreEnclosed">
    /// Besides surfaces coming within the tolerance, a pair is also in contact when the first's mesh
    /// encloses the centre of the second's mesh bounds.
    /// </param>
    public (List<IndexPair> Found, PairTestStats Work) FindFirstInContact(IReadOnlyList<IndexPair> pairs, bool alsoWhenCentreEnclosed, Execution execution)
    {
        // GroupBy yields groups in the order their keys first appear and keeps the input order inside each group.
        var pairsBySecond = Enumerable.Range(0, pairs.Count).GroupBy(pairIndex => pairs[pairIndex].Second);
        var (foundIndices, work) = ParallelMap.FirstMatchPerGroup(
            execution,
            [.. pairsBySecond.Select(group => (IReadOnlyList<int>)group.ToList())],
            () => new ContactScratch(),
            (pairIndex, scratch) => IsInContact(pairs[pairIndex], alsoWhenCentreEnclosed, scratch),
            scratch => scratch.Harvest(),
            GroupsPerRange);
        return ([.. foundIndices.Select(pairIndex => pairs[pairIndex])], work);
    }

    private bool IsInContact(IndexPair pair, bool alsoWhenCentreEnclosed, ContactScratch scratch)
    {
        var touch = scratch.Count(TestPair(pair, scratch.Touch));
        if (touch == PairTouch.Touching) return true;
        return touch == PairTouch.Apart && alsoWhenCentreEnclosed && EnclosesCentreOfSecond(pair, scratch.Enclosure);
    }

    /// <summary>Minimum surface distance of a pair known to touch, world units; NaN when either mesh has no usable triangles.</summary>
    public float MeasureMinSurfaceDistance(IndexPair pair, TouchScratch scratch) =>
        MeasureMeshes(
            pair, (tolerance, scratch), float.NaN,
            static (first, firstPlacement, second, secondPlacement, state) =>
                MeshContact.MinSurfaceDistance(first, firstPlacement, second, secondPlacement, state.tolerance, state.scratch));

    private PairTouch TestPair(IndexPair pair, TouchScratch scratch) =>
        MeasureMeshes(
            pair, (tolerance, scratch), PairTouch.NoGeometry,
            static (first, firstPlacement, second, secondPlacement, state) =>
                MeshContact.SurfacesTouch(first, firstPlacement, second, secondPlacement, state.tolerance, state.scratch) ? PairTouch.Touching : PairTouch.Apart);

    private bool EnclosesCentreOfSecond(IndexPair pair, List<int> scratch) =>
        MeasureMeshes(
            pair, scratch, false,
            static (enclosing, enclosingPlacement, enclosed, enclosedPlacement, state) =>
                MeshContact.PointEnclosed(enclosing, RelativeTransform.Create(from: enclosedPlacement, to: enclosingPlacement).Apply(enclosed.Bounds.Center), state));

    /// <summary>
    /// The measure of the pair's two meshes, each with its placement, both held while measured; <paramref name="withoutGeometry"/>
    /// when either mesh has no usable triangles. The state is handed to <paramref name="measure"/> so that it can be a static
    /// lambda: this runs for every pair on the parallel path, where a closure would be allocated per pair.
    /// </summary>
    private T MeasureMeshes<TState, T>(
        IndexPair pair, TState state, T withoutGeometry, Func<MeshTriangleTree, PlacedTransform, MeshTriangleTree, PlacedTransform, TState, T> measure)
    {
        using var first = meshes.Acquire(pathOf(pair.First));
        using var second = meshes.Acquire(pathOf(pair.Second));
        if (first.Value is not { } firstMesh || second.Value is not { } secondMesh) return withoutGeometry;
        return measure(firstMesh, transformOf(pair.First), secondMesh, transformOf(pair.Second), state);
    }
}
