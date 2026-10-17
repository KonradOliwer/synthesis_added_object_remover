using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The real base-facts reader over in-memory records, with the link cache's plugins as the known plugin names.</summary>
internal static class TestBaseFacts
{
    /// <summary>Base facts for no base record: every base is unresolved, so no base has a script.</summary>
    public static readonly IBaseFacts Unresolved = new UnresolvedBases();

    public static BaseFactsReader Reader(ILinkCache linkCache) =>
        new(linkCache, new KnownPluginNames(linkCache.ListedOrder.Select(mod => mod.ModKey.ToPluginName())));

    public static IBaseFactsFactory Factory(ILinkCache linkCache, Func<IBaseFacts, IBaseFacts>? wrap = null) =>
        new OpeningFactory(() => wrap is null ? Reader(linkCache) : wrap(Reader(linkCache)));

    private sealed class OpeningFactory(Func<IBaseFacts> open) : IBaseFactsFactory
    {
        public IBaseFacts Open() => open();
    }

    private sealed class UnresolvedBases : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => BaseFacts.Unresolved(baseKey.Record);
    }
}
