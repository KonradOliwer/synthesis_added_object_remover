using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Placed objects of any plugin other than the target objects, indexed per space on first query;
/// visibility judged lazily. Thread-safe.
/// </summary>
internal sealed class SupporterIndex(
    IReadOnlyDictionary<FormKey, List<OtherObject>> objectsBySpace, ShapeCatalog shapes, ParallelOptions parallelOptions)
{
    private readonly LazyCache<FormKey, OtherObjectIndex> _bySpace = new();

    public OtherObjectIndex GetSpace(FormKey spaceKey) =>
        _bySpace.GetOrCreate(
            spaceKey, () => OtherObjectIndex.CreateUncounted(objectsBySpace.GetValueOrDefault(spaceKey) ?? [], shapes, parallelOptions));

    /// <summary>The spaces indexed so far.</summary>
    public IEnumerable<OtherObjectIndex> GetIndexedSpaces() => _bySpace.GetCreatedValues();
}
