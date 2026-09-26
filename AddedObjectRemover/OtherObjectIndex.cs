using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Other-mod objects of one space, indexed by their raw position only. Each object's true bounds
/// center (which may need a mesh read) is computed at most once, lazily, the first time a query
/// turns the object up, so objects that are never candidates are never measured. Objects whose
/// base is invisible are found the same lazy way and then never match.
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
    private readonly ReasonCounter _invisible;
    private readonly Vector3[] _centers;
    private readonly int[] _state;

    /// <summary>
    /// 0/1 per object: set when the object is judged a same-position/same-size replacement of a
    /// target object. Replaced objects stay in <see cref="Grid"/> (they are replacement candidates)
    /// but never match in the too-close test.
    /// </summary>
    private readonly int[] _replaced;

    private OtherObjectIndex(IReadOnlyList<OtherObject> objects, BaseObjectShapeProvider shapes, ReasonCounter invisible)
    {
        _objects = objects.ToArray();
        _shapes = shapes;
        _invisible = invisible;
        _centers = new Vector3[_objects.Length];
        _state = new int[_objects.Length];
        _replaced = new int[_objects.Length];
        Grid = SpatialGrid.FromPoints(_objects.Select(o => o.Position).ToArray());
    }

    /// <summary>
    /// One index per space containing target objects (empty if it has no other objects). The
    /// scan's per-space lists are released afterwards.
    /// </summary>
    public static Dictionary<FormKey, OtherObjectIndex> BuildForTargetSpaces(
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        ReasonCounter invisible)
    {
        var indexes = new Dictionary<FormKey, OtherObjectIndex>();
        foreach (var target in scan.Targets)
        {
            if (indexes.ContainsKey(target.SpaceKey)) continue;
            indexes[target.SpaceKey] = new OtherObjectIndex(
                scan.OthersBySpace.TryGetValue(target.SpaceKey, out var others) ? others : [],
                shapes,
                invisible);
        }
        scan.OthersBySpace.Clear();
        scan.OthersBySpace.TrimExcess();
        return indexes;
    }

    public SpatialGrid Grid { get; }

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

    private int MeasureAndPublish(int index)
    {
        ref readonly var other = ref _objects[index];
        var reason = other.HasMapMarker ? "map marker reference" : _shapes.GetInvisibleReason(other.Base, other.IsPrimitive);
        int state;
        if (reason != null)
        {
            state = Invisible;
        }
        else
        {
            var transform = new PlacedTransform(other.Position, Geometry.RotationFromEuler(other.Rotation), other.Scale);
            var point = Geometry.WorldBoundsCenter(_shapes.GetLocalBox(other.Base), transform);
            _centers[index] = Geometry.IsFinite(point) ? point : other.Position;
            state = Visible;
        }

        if (Interlocked.CompareExchange(ref _state[index], state, NotMeasured) == NotMeasured && reason != null)
        {
            _invisible.Add(reason);
        }
        return state;
    }
}
