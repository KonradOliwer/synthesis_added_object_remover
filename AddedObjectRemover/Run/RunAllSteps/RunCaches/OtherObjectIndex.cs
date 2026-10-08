using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// Placed objects of one space, indexed by their raw position (<see cref="PositionGrid"/>) and by
/// their world bounds (<see cref="Bounds"/>, built on first query for the whole space). Each object's
/// visibility and true bounds center are worked out at most once, lazily, the first time a query
/// turns the object up. Objects whose base is invisible are found the same lazy way.
///
/// Thread-safe: two threads measuring the same object concurrently get identical values.
/// </summary>
internal sealed class OtherObjectIndex : IOtherObjectIndex
{
    private readonly record struct Measurement(bool IsVisible, Vector3 Center);

    private readonly IReadOnlyList<OtherObject> _objects;
    private readonly IBaseObjectShapes _shapes;
    private readonly IndexMemo<Measurement> _measurements;
    private readonly Action<OtherObject, Exception>? _onMeasureFailed;

    private OtherObjectIndex(ItemsInSpace<OtherObject> bounds, IBaseObjectShapes shapes, Action<OtherObject, Exception>? onMeasureFailed)
    {
        Bounds = bounds;
        _objects = bounds.Items;
        _shapes = shapes;
        _onMeasureFailed = onMeasureFailed;
        _measurements = new IndexMemo<Measurement>(_objects.Count);
        PositionGrid = GridCandidates.OfPoints(_objects.Select(o => o.Position).ToArray());
    }

    /// <param name="onMeasureFailed">Told when measuring an object fails unexpectedly, which makes the object count as invisible; null lets the failure through.</param>
    public static OtherObjectIndex Create(ItemsInSpace<OtherObject> bounds, IBaseObjectShapes shapes, Action<OtherObject, Exception>? onMeasureFailed) =>
        new(bounds, shapes, onMeasureFailed);

    /// <summary>The objects by raw position: only for matching objects by where they are placed.</summary>
    public GridCandidates PositionGrid { get; }

    /// <summary>The objects by world bounds: for every question about what an object's bounds reach.</summary>
    public ItemsInSpace<OtherObject> Bounds { get; }

    public OtherObject this[int index] => _objects[index];

    public OrientedBox OrientedBoxOf(int index) => OrientedBox.FromLocal(_shapes.Of(_objects[index].Base).Box, _objects[index].Transform);

    /// <summary>World-space bounds center of a visible object; false (and no center) for an invisible one.</summary>
    public bool TryGetVisibleCenter(int index, out Vector3 center)
    {
        var measurement = _measurements.GetOrCompute(index, Measure);
        center = measurement.Center;
        return measurement.IsVisible;
    }

    public bool IsVisible(int index) => TryGetVisibleCenter(index, out _);

    private Measurement Measure(int index)
    {
        try
        {
            return MeasureObject(_objects[index]);
        }
        catch (Exception ex) when (_onMeasureFailed != null && Failures.IsRecoverable(ex))
        {
            _onMeasureFailed(_objects[index], ex);
            return new Measurement(IsVisible: false, Center: default);
        }
    }

    private Measurement MeasureObject(OtherObject other)
    {
        var visibility = _shapes.VisibilityOf(other.Base, other.IsPrimitive, other.HasMapMarker);
        if (!visibility.IsVisible) return new Measurement(IsVisible: false, Center: default);

        var point = Boxes.WorldBoundsCenter(_shapes.Of(other.Base).Box, other.Transform);
        return new Measurement(IsVisible: true, Center: Vectors.IsFinite(point) ? point : other.Position);
    }
}
