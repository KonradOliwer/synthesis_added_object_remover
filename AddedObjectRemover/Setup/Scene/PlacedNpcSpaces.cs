using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The placed NPCs of each space with their possible bodies, replaced ones included, indexed per
/// space on first use. Thread-safe.
/// </summary>
internal sealed class PlacedNpcSpaces(PlacedSpaces rivals, NpcBodyCache bodies, Execution execution)
{
    private readonly LazyCache<FormKey, PlacedNpcIndex> _bySpace = new();

    public PlacedNpcIndex IndexOf(FormKey space) =>
        _bySpace.GetOrCreate(space, () => PlacedNpcIndex.Build(rivals.ObjectsIn(space), bodies, execution));
}
