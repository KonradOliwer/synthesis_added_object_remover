using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <param name="BodiesBuilt">Bodies resolved per (NPC, sex).</param>
/// <param name="BasesResolved">Placed bases (NPCs or leveled lists) resolved through their templates.</param>
internal readonly record struct NpcBodyCacheStats(int BodiesBuilt, int BodiesReused, int BasesResolved, int BasesReused);

/// <summary>
/// Thread-safe cache of NPC bodies, failures included: per placed base (NPC or leveled list) the
/// resolved body, and per (NPC supplying its own traits, sex) the body it has. A base that can be
/// several NPCs (leveled lists, templates pointing at one) gets the largest body by box volume,
/// so an NPC is never assumed smaller than it may be; NPCs of unknown size are left out of that
/// choice while a sized one exists. Body mesh triangles live in the bounded <see cref="TriangleTreeCache"/>.
/// </summary>
internal sealed class NpcBodyCache(NpcBodyResolver resolver)
{
    private readonly LazyCache<FormKey, NpcBody> _byBase = new();
    private readonly LazyCache<(FormKey Npc, bool Female), NpcBody> _byTraitSource = new();

    private int _baseRequests;
    private int _basesResolved;
    private int _bodyRequests;
    private int _bodiesBuilt;

    public NpcBodyCacheStats GetStats()
    {
        var basesResolved = Volatile.Read(ref _basesResolved);
        var bodiesBuilt = Volatile.Read(ref _bodiesBuilt);
        return new NpcBodyCacheStats(
            bodiesBuilt,
            Volatile.Read(ref _bodyRequests) - bodiesBuilt,
            basesResolved,
            Volatile.Read(ref _baseRequests) - basesResolved);
    }

    public NpcBody GetBody(FormKey placedBase)
    {
        Interlocked.Increment(ref _baseRequests);
        return _byBase.GetOrCreate(placedBase, () => ResolveBase(placedBase));
    }

    private NpcBody ResolveBase(FormKey placedBase)
    {
        Interlocked.Increment(ref _basesResolved);
        var sources = resolver.CollectTraitSources(placedBase);
        if (sources.Count == 0) return NpcBody.Unknown("no NPC found through its templates and leveled lists");
        return SelectLargest(sources.Select(GetOwnBody).ToList());
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

    /// <summary>Ties keep the first body, so the choice follows the record order.</summary>
    private static NpcBody SelectLargest(IReadOnlyList<NpcBody> bodies)
    {
        NpcBody? largest = null;
        foreach (var body in bodies.Where(body => body.IsSized))
        {
            if (largest == null || body.Volume > largest.Volume) largest = body;
        }
        return largest ?? bodies[0];
    }
}
