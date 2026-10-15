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
    private readonly IReadOnlyList<OtherObject> _objects;
    private readonly IBaseObjectShapes _shapes;
    private readonly IndexMemo<ObjectMeasurement> _measurements;
    private readonly Func<IBaseObjectShapes, OtherObject, ObjectMeasurement> _measure;
    private readonly ObjectContainment _containment;

    private OtherObjectIndex(
        ItemsInSpace<OtherObject> bounds, IBaseObjectShapes shapes, Func<IBaseObjectShapes, OtherObject, ObjectMeasurement> measure, ObjectContainment containment)
    {
        Bounds = bounds;
        _objects = bounds.Items;
        _shapes = shapes;
        _measure = measure;
        _containment = containment;
        _measurements = new IndexMemo<ObjectMeasurement>(_objects.Count);
        PositionGrid = GridCandidates.OfPoints(_objects.Select(o => o.Position).ToArray());
    }

    /// <param name="measure">Decides, once per object, whether the object is visible and where its bounds centre is.</param>
    public static OtherObjectIndex Create(
        ItemsInSpace<OtherObject> bounds, IBaseObjectShapes shapes, Func<IBaseObjectShapes, OtherObject, ObjectMeasurement> measure, ObjectContainment containment) =>
        new(bounds, shapes, measure, containment);

    /// <summary>The objects by raw position: only for matching objects by where they are placed.</summary>
    public GridCandidates PositionGrid { get; }

    /// <summary>The objects by world bounds: for every question about what an object's bounds reach.</summary>
    public ItemsInSpace<OtherObject> Bounds { get; }

    public OtherObject this[int index] => _objects[index];

    public OrientedBox OrientedBoxOf(int index) => OrientedBox.FromLocal(_shapes.Of(_objects[index].Base).Box, _objects[index].Transform);

    /// <summary>World-space bounds center of a visible object; false (and no center) for an invisible one.</summary>
    public bool TryGetVisibleCenter(int index, out Vector3 center)
    {
        var measurement = _measurements.GetOrCompute(index, slot => _measure(_shapes, _objects[slot]));
        center = measurement.Center;
        return measurement.IsVisible;
    }

    public bool IsVisible(int index) => TryGetVisibleCenter(index, out _);

    public int FindContainingVisible(Vector3 worldPoint, Func<OtherObject, bool> include, ObjectQueryScratch scratch) =>
        Bounds.FirstContaining(
            worldPoint,
            include: slot => include(_objects[slot]),
            contains: slot => IsVisible(slot) && _containment.Contains(_objects[slot].Base, _objects[slot].Transform, worldPoint, scratch),
            scratch.Spatial);
}
