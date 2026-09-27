using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <param name="BodiesBuilt">Bodies resolved per (NPC, sex).</param>
/// <param name="BasesResolved">Placed NPC bases resolved through their templates.</param>
/// <param name="ListsResolved">Leveled lists resolved to the NPCs they can spawn.</param>
internal readonly record struct NpcBodyCacheStats(
    int BodiesBuilt,
    int BodiesReused,
    int BasesResolved,
    int BasesReused,
    int ListsResolved,
    int ListsReused);

/// <summary>
/// Thread-safe cache of NPC bodies, point fallbacks included, built once each and shared by all
/// threads: per placed base and per leveled list every distinct body it can have
/// (<see cref="NpcBodySet"/>), and per (NPC supplying its own traits, sex) the body it has. A base
/// or list that resolves to no NPC at all has no body set.
/// </summary>
internal sealed class NpcBodyCache(NpcBodyResolver resolver)
{
    private readonly LazyCache<FormKey, NpcBodySet?> _byBase = new();
    private readonly LazyCache<FormKey, NpcBodySet?> _byList = new();
    private readonly LazyCache<(FormKey Npc, bool Female), NpcBody> _byTraitSource = new();

    private int _baseRequests;
    private int _basesResolved;
    private int _listRequests;
    private int _listsResolved;
    private int _bodyRequests;
    private int _bodiesBuilt;

    public NpcBodyCacheStats GetStats()
    {
        var basesResolved = Volatile.Read(ref _basesResolved);
        var listsResolved = Volatile.Read(ref _listsResolved);
        var bodiesBuilt = Volatile.Read(ref _bodiesBuilt);
        return new NpcBodyCacheStats(
            bodiesBuilt,
            Volatile.Read(ref _bodyRequests) - bodiesBuilt,
            basesResolved,
            Volatile.Read(ref _baseRequests) - basesResolved,
            listsResolved,
            Volatile.Read(ref _listRequests) - listsResolved);
    }

    /// <summary>Null when no NPC is found through the base's templates and leveled lists.</summary>
    public NpcBodySet? GetBodies(FormKey placedBase)
    {
        Interlocked.Increment(ref _baseRequests);
        return _byBase.GetOrCreate(placedBase, () => ResolveBase(placedBase));
    }

    private NpcBodySet? ResolveBase(FormKey placedBase)
    {
        Interlocked.Increment(ref _basesResolved);
        return resolver.FindTraitSupplier(placedBase) switch
        {
            INpcGetter npc => NpcBodySet.Of([GetOwnBody(npc)]),
            ILeveledNpcGetter list => GetListBodies(list),
            _ => null,
        };
    }

    private NpcBodySet? GetListBodies(ILeveledNpcGetter list)
    {
        Interlocked.Increment(ref _listRequests);
        return _byList.GetOrCreate(list.FormKey, () => ResolveList(list));
    }

    private NpcBodySet? ResolveList(ILeveledNpcGetter list)
    {
        Interlocked.Increment(ref _listsResolved);
        var sources = resolver.CollectTraitSources(list);
        return sources.Count == 0 ? null : NpcBodySet.Of(sources.Select(GetOwnBody).ToList());
    }

    private NpcBody GetOwnBody(INpcGetter npc)
    {
        var female = NpcBodyResolver.IsFemale(npc);
        Interlocked.Increment(ref _bodyRequests);
        return _byTraitSource.GetOrCreate((npc.FormKey, female), () => BuildOwnBody(npc, female));
    }

    private NpcBody BuildOwnBody(INpcGetter npc, bool female)
    {
        Interlocked.Increment(ref _bodiesBuilt);
        return resolver.ResolveOwnBody(npc, female);
    }
}
