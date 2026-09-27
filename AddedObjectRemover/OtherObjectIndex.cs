using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Placed objects of one space, indexed by their raw position (<see cref="PositionGrid"/>) and by
/// their world bounds (<see cref="Bounds"/>, built on first use for the whole space). Each object's
/// visibility and true bounds center are worked out at most once, lazily, the first time a query
/// turns the object up. Objects whose base is invisible are found the same lazy way and then never match.
///
/// Thread-safe: the center is written before its state is published with an interlocked store,
/// and read only after a load sees the state. Two threads measuring the same object concurrently
/// write identical values, and only the one that publishes the state counts it.
/// </summary>
internal sealed class OtherObjectIndex
{
    private const int NotMeasured = 0;
    private const int Visible = 1;
    private const int Invisible = 2;

    private readonly OtherObject[] _objects;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly ReasonCounter? _invisible;
    private readonly Vector3[] _centers;
    private readonly int[] _state;

    /// <summary>
    /// 0/1 per object: set when the object is judged a same-position/same-size replacement of a
    /// target object. Replaced objects stay indexed (they are replacement candidates) but never
    /// match in the too-close test.
    /// </summary>
    private readonly int[] _replaced;

    private readonly Lazy<OtherObjectBoxIndex> _bounds;

    /// <param name="invisible">Counts each invisible object once per reason; null counts nothing.</param>
    private OtherObjectIndex(
        IReadOnlyList<OtherObject> objects, BaseObjectShapeProvider shapes, ReasonCounter? invisible, ParallelOptions parallelOptions)
    {
        _objects = objects.ToArray();
        _shapes = shapes;
        _invisible = invisible;
        _centers = new Vector3[_objects.Length];
        _state = new int[_objects.Length];
        _replaced = new int[_objects.Length];
        PositionGrid = SpatialGrid.FromPoints(_objects.Select(o => o.Position).ToArray());
        _bounds = new Lazy<OtherObjectBoxIndex>(
            () => OtherObjectBoxIndex.Build(_objects, shapes, parallelOptions), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// One index per space containing target objects (empty if it has no other objects). The
    /// scan's per-space lists are released afterwards.
    /// </summary>
    public static Dictionary<FormKey, OtherObjectIndex> BuildForTargetSpaces(
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        ReasonCounter invisible,
        ParallelOptions parallelOptions)
    {
        var indexes = new Dictionary<FormKey, OtherObjectIndex>();
        foreach (var target in scan.Targets)
        {
            if (indexes.ContainsKey(target.SpaceKey)) continue;
            indexes[target.SpaceKey] = new OtherObjectIndex(
                scan.OthersBySpace.TryGetValue(target.SpaceKey, out var others) ? others : [],
                shapes,
                invisible,
                parallelOptions);
        }
        scan.OthersBySpace.Clear();
        scan.OthersBySpace.TrimExcess();
        return indexes;
    }

    public static OtherObjectIndex CreateUncounted(
        IReadOnlyList<OtherObject> objects, BaseObjectShapeProvider shapes, ParallelOptions parallelOptions) =>
        new(objects, shapes, invisible: null, parallelOptions);

    /// <summary>The objects by raw position: only for matching objects by where they are placed.</summary>
    public SpatialGrid PositionGrid { get; }

    /// <summary>The objects by world bounds: for every question about what an object's bounds reach.</summary>
    public OtherObjectBoxIndex Bounds => _bounds.Value;

    public int Count => _objects.Length;

    public OtherObject this[int index] => _objects[index];

    /// <summary>True only for the first caller.</summary>
    public bool TryMarkReplaced(int index) => Interlocked.CompareExchange(ref _replaced[index], 1, 0) == 0;

    public bool IsReplaced(int index) => Volatile.Read(ref _replaced[index]) != 0;

    /// <summary>World-space bounds center of a visible object; false (and no center) for an invisible one.</summary>
    public bool TryGetVisibleCenter(int index, out Vector3 center)
    {
        var state = Volatile.Read(ref _state[index]);
        if (state == NotMeasured) state = MeasureAndPublish(index);
        center = _centers[index];
        return state == Visible;
    }

    public bool IsVisible(int index) => TryGetVisibleCenter(index, out _);

    private int MeasureAndPublish(int index)
    {
        ref readonly var other = ref _objects[index];
        var visibility = _shapes.GetVisibility(other.Base, other.IsPrimitive, other.HasMapMarker);
        if (visibility.IsVisible)
        {
            var point = Geometry.WorldBoundsCenter(_shapes.GetLocalBox(other.Base), other.Transform);
            _centers[index] = Geometry.IsFinite(point) ? point : other.Position;
        }

        var state = visibility.IsVisible ? Visible : Invisible;
        if (Interlocked.CompareExchange(ref _state[index], state, NotMeasured) == NotMeasured && !visibility.IsVisible)
        {
            _invisible?.Add(visibility.Describe());
        }
        return state;
    }
}
