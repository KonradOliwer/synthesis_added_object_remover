using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Placed objects of any plugin, apart from the target objects themselves, that may hold up an
/// Anchoring candidate. A space is indexed on its first query, so only spaces with candidates are
/// ever indexed, and each object's visibility is judged lazily. Thread-safe.
/// </summary>
internal sealed class SupporterIndex(IReadOnlyDictionary<FormKey, List<OtherObject>> objectsBySpace, BaseObjectShapeProvider shapes)
{
    private readonly LazyCache<FormKey, OtherObjectIndex> _bySpace = new();

    public OtherObjectIndex GetSpace(FormKey spaceKey) =>
        _bySpace.GetOrCreate(spaceKey, () => OtherObjectIndex.CreateUncounted(objectsBySpace.GetValueOrDefault(spaceKey) ?? [], shapes));
}
