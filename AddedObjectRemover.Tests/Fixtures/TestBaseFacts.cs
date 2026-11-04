using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The real base-facts reader over in-memory records, with the link cache's plugins as the known plugin names.</summary>
internal static class TestBaseFacts
{
    /// <summary>Base facts for no base record: every base is unresolved, so no base has a script.</summary>
    public static readonly IBaseFacts Unresolved = new UnresolvedBases();

    /// <summary>Base facts in which every base is resolved plain scenery without distant LOD, so a moved object of any base may be copied.</summary>
    public static readonly IBaseFacts PlainScenery = new PlainSceneryBases();

    public static BaseFactsReader Reader(ILinkCache linkCache) =>
        new(linkCache, new KnownPluginNames(linkCache.ListedOrder.Select(mod => mod.ModKey.ToPluginName())));

    private sealed class UnresolvedBases : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => BaseFacts.Unresolved(baseKey.Record);
    }

    private sealed class PlainSceneryBases : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => BaseFacts.Unresolved(baseKey.Record) with { Resolved = true, IsPlainScenery = true };
    }
}
