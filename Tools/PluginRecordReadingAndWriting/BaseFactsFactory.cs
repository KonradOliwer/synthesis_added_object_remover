using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover;

/// <summary>Created in the entry from the Synthesis state's link cache and the run's known plugin names.</summary>
public sealed class BaseFactsFactory(ILinkCache linkCache, KnownPluginNames knownPlugins) : IBaseFactsFactory
{
    public IBaseFacts Open() => new BaseFactsReader(linkCache, knownPlugins);
}
