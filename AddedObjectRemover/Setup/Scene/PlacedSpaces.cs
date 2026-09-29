using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Placed objects grouped by space, each space indexed on first use. Thread-safe.</summary>
internal sealed class PlacedSpaces
{
    private readonly IReadOnlyList<OtherObject> _objects;
    private readonly Dictionary<FormKey, OtherObject[]> _bySpace;
    private readonly int[] _slotOf;
    private readonly ShapeCatalog _shapes;
    private readonly Execution _execution;
    private readonly IPhaseTimer _timer;
    private readonly TimedPhase _boundsPhase;
    private readonly LazyCache<FormKey, OtherObjectIndex> _indexes = new();

    /// <param name="objects">By <see cref="OtherId"/>: the object at position i has id i.</param>
    public PlacedSpaces(IReadOnlyList<OtherObject> objects, ShapeCatalog shapes, Execution execution, IPhaseTimer timer, TimedPhase boundsPhase)
    {
        _objects = objects;
        _shapes = shapes;
        _execution = execution;
        _timer = timer;
        _boundsPhase = boundsPhase;
        _bySpace = objects.GroupBy(other => other.SpaceKey).ToDictionary(group => group.Key, group => group.ToArray());
        _slotOf = new int[objects.Count];
        foreach (var space in _bySpace.Values)
        {
            for (var slot = 0; slot < space.Length; slot++) _slotOf[space[slot].Id.Index] = slot;
        }
    }

    public OtherObject Get(OtherId id) => _objects[id.Index];

    /// <summary>The objects of the space, in <see cref="OtherId"/> order.</summary>
    public IReadOnlyList<OtherObject> ObjectsIn(FormKey space) => _bySpace.GetValueOrDefault(space) ?? [];

    /// <summary>The index of the space; its slots follow <see cref="ObjectsIn"/>.</summary>
    public OtherObjectIndex IndexOf(FormKey space) =>
        _indexes.GetOrCreate(space, () => OtherObjectIndex.Create(ObjectsIn(space), _shapes, _execution, _timer, _boundsPhase));

    /// <summary>The index of the object's space and its slot there.</summary>
    public (OtherObjectIndex Index, int Slot) Locate(OtherId id) => (IndexOf(Get(id).SpaceKey), _slotOf[id.Index]);
}
