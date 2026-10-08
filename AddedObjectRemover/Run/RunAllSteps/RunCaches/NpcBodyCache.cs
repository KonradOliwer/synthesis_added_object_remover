using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// Thread-safe cache of NPC bodies, point fallbacks included, built once each and shared by all
/// threads: per placed base and per leveled list every distinct body it can have
/// (<see cref="NpcBodySet"/>), and per NPC supplying its own traits the body it has. A base
/// or list that resolves to no NPC at all has no body set. Which template flag makes an NPC take
/// over its template's traits is the caller's rule, so it is passed in.
/// </summary>
internal sealed class NpcBodyCache(IPluginRecords plugin, NpcTemplateFlag templateFlag, NpcBodyResolver resolver) : INpcBodies
{
    private readonly ComputedOncePerKey<RecordKey, NpcBodySet?> _byBase = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);
    private readonly ComputedOncePerKey<RecordKey, NpcBodySet?> _byList = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);
    private readonly ComputedOncePerKey<RecordKey, NpcBody> _byTraitSource = new(Publication.BuiltOnce, EqualityComparer<RecordKey>.Default);

    public NpcBodyCacheStats GetStats() =>
        new(_byTraitSource.Contents().Count, _byBase.Contents().Count, _byList.Contents().Count);

    public NpcBodySet? GetBodies(RecordKey placedBase) => _byBase.Get(placedBase, () => ResolveBase(placedBase));

    private NpcBodySet? ResolveBase(RecordKey placedBase) =>
        plugin.TraitSupplierOf(new BaseKey(placedBase, BaseLinkKind.Npc), templateFlag) switch
        {
            { Kind: TraitSupplierKind.Npc } supplier => NpcBodyResolver.DistinctBodies([GetOwnBody(supplier.Record)]),
            { Kind: TraitSupplierKind.LeveledList } supplier => GetListBodies(supplier.Record),
            _ => null,
        };

    private NpcBodySet? GetListBodies(RecordKey list) =>
        _byList.Get(list, () => ResolveList(list));

    private NpcBodySet? ResolveList(RecordKey list)
    {
        var sources = plugin.LeveledEntriesOf(list, templateFlag);
        return sources.Count == 0 ? null : NpcBodyResolver.DistinctBodies(sources.Select(GetOwnBody).ToList());
    }

    private NpcBody GetOwnBody(RecordKey npc) =>
        _byTraitSource.Get(npc, () => resolver.ResolveOwnBody(ReadTraits(npc)));

    private NpcTraits ReadTraits(RecordKey npc) =>
        plugin.NpcTraitsOf(new BaseKey(npc, BaseLinkKind.Npc))
            ?? throw new InvalidOperationException($"The NPC {npc} that supplies its own traits was not found.");
}
