namespace AddedObjectRemover;

/// <summary>Created in the entry from the final world's known plugin names; each run's reader resolves through that run's link cache.</summary>
public sealed class BaseFactsFactory(KnownPluginNames knownPlugins) : IBaseFactsFactory
{
    public IBaseFacts Open(IRecordIndex records) => new BaseFactsReader(RunLinkCache.CacheOf(records), knownPlugins);

    public IReadOnlyDictionary<uint, CollisionLayerFact> ReadCollisionLayers(IRecordIndex records) => CollisionLayerReader.Read(RunLinkCache.CacheOf(records));
}
