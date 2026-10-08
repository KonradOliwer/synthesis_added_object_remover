using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>Placed objects grouped by space, each space indexed on first use. Thread-safe.</summary>
internal sealed class PlacedSpaces : IOtherModObjectsBySpace, IObjectsOfAnyPluginBySpace
{
    /// <summary>
    /// Growth of every indexed AABB: absorbs the rounding between this AABB and the exact tests'
    /// own transforms, so the AABB filter never rejects a true hit.
    /// </summary>
    private const float RoundingSlack = 1f;

    private readonly IReadOnlyList<OtherObject> _objects;
    private readonly ItemsBySpace<OtherObject> _bySpace;
    private readonly int[] _slotOf;
    private readonly IBaseObjectShapes _shapes;
    private readonly Action<OtherObject, Exception>? _onMeasureFailed;
    private readonly ComputedOncePerKey<RecordKey, OtherObjectIndex> _indexes = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    /// <param name="objects">By <see cref="OtherId"/>: the object at position i has id i.</param>
    /// <param name="onMeasureFailed">See <see cref="OtherObjectIndex.Create"/>.</param>
    public PlacedSpaces(IReadOnlyList<OtherObject> objects, IBaseObjectShapes shapes, Action<OtherObject, Exception>? onMeasureFailed, Execution execution)
    {
        _objects = objects;
        _shapes = shapes;
        _onMeasureFailed = onMeasureFailed;
        _bySpace = ItemsBySpace<OtherObject>.Create(
            objects, other => other.SpaceKey, WorldAabbOf, isIncluded: _ => true, EqualityComparer<RecordKey>.Default, execution);
        _slotOf = new int[objects.Count];
        foreach (var space in _bySpace.Spaces)
        {
            for (var slot = 0; slot < space.Items.Count; slot++) _slotOf[space.Items[slot].Id.Index] = slot;
        }
    }

    public OtherObject Get(OtherId id) => _objects[id.Index];

    /// <summary>The objects of the space, in <see cref="OtherId"/> order.</summary>
    public IReadOnlyList<OtherObject> ObjectsIn(RecordKey space) => _bySpace.In(space).Items;

    /// <summary>The index of the space; its slots follow <see cref="ObjectsIn"/>.</summary>
    public IOtherObjectIndex IndexOf(RecordKey space) =>
        _indexes.Get(space, () => OtherObjectIndex.Create(_bySpace.In(space), _shapes, _onMeasureFailed));

    /// <summary>The index of the object's space and its slot there.</summary>
    public (IOtherObjectIndex Index, int Slot) Locate(OtherId id) => (IndexOf(Get(id).SpaceKey), _slotOf[id.Index]);

    private Box WorldAabbOf(OtherObject other) =>
        OrientedBox.FromLocal(_shapes.Of(other.Base).Box, other.Transform).WorldAabb(RoundingSlack);
}
