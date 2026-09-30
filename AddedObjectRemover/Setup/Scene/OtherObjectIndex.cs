using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Placed objects of one space, indexed by their raw position (<see cref="PositionGrid"/>) and by
/// their world bounds (<see cref="Bounds"/>, built on first use for the whole space). Each object's
/// visibility and true bounds center are worked out at most once, lazily, the first time a query
/// turns the object up. Objects whose base is invisible are found the same lazy way.
///
/// Thread-safe: the center is written before its state is published with an interlocked store,
/// and read only after a load sees the state. Two threads measuring the same object concurrently
/// write identical values.
/// </summary>
internal sealed class OtherObjectIndex
{
    private const int NotMeasured = 0;
    private const int Visible = 1;
    private const int Invisible = 2;

    private readonly OtherObject[] _objects;
    private readonly ShapeCatalog _shapes;
    private readonly Vector3[] _centers;
    private readonly int[] _state;

    private readonly Lazy<OtherObjectBoxIndex> _bounds;

    private OtherObjectIndex(
        IReadOnlyList<OtherObject> objects, ShapeCatalog shapes, Execution execution, IPhaseTimer timer, TimedPhase boundsPhase)
    {
        _objects = objects.ToArray();
        _shapes = shapes;
        _centers = new Vector3[_objects.Length];
        _state = new int[_objects.Length];
        PositionGrid = SpatialGrid.FromPoints(_objects.Select(o => o.Position).ToArray());
        _bounds = new Lazy<OtherObjectBoxIndex>(() => timer.Time(boundsPhase, () => OtherObjectBoxIndex.Build(_objects, _shapes, execution)), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <param name="boundsPhase">The phase under which building <see cref="Bounds"/> is timed.</param>
    public static OtherObjectIndex Create(
        IReadOnlyList<OtherObject> objects, ShapeCatalog shapes, Execution execution, IPhaseTimer timer, TimedPhase boundsPhase) =>
        new(objects, shapes, execution, timer, boundsPhase);

    /// <summary>The objects by raw position: only for matching objects by where they are placed.</summary>
    public SpatialGrid PositionGrid { get; }

    /// <summary>The objects by world bounds: for every question about what an object's bounds reach.</summary>
    public OtherObjectBoxIndex Bounds => _bounds.Value;

    public OtherObject this[int index] => _objects[index];

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
        Interlocked.CompareExchange(ref _state[index], state, NotMeasured);
        return state;
    }
}
