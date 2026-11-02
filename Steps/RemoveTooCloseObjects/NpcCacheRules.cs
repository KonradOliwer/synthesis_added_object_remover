using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The step's rules for the body of an NPC, which the caches of NPC bodies and of placed NPCs apply.</summary>
internal static class NpcCacheRules
{
    /// <summary>How an NPC's own body is resolved, and which of its possible bodies are told apart.</summary>
    public static NpcBodyRules Bodies { get; } = new(NpcTemplateFlag.Traits, CreateOwnBodyResolver, DistinctBodies);

    /// <summary>Which placed objects are NPCs, how their bodies stand, and how their sizes are counted.</summary>
    public static PlacedNpcRules PlacedNpcs { get; } = new(IsPlacedNpc, BodyTransform, CountSizes);

    private static Func<NpcTraits, NpcBody> CreateOwnBodyResolver(IMeshFiles meshFiles, IBodyMeshBounds bodyBounds) =>
        new NpcBodyResolver(meshFiles, bodyBounds).ResolveOwnBody;

    /// <summary>Bodies with the same box are tested once; the first of them is kept.</summary>
    private static NpcBodySet DistinctBodies(IReadOnlyList<NpcBody> possibleBodies)
    {
        var distinct = possibleBodies.DistinctBy(body => body.LocalBox).ToList();
        return new NpcBodySet(distinct, Box.UnionAll(distinct.Select(body => body.LocalBox)));
    }

    private static bool IsPlacedNpc(OtherObject other) => other.IsPlacedNpc;

    /// <summary>Actors stand upright, so a body turns only about Z.</summary>
    private static PlacedTransform BodyTransform(OtherObject npc) =>
        PlacedTransform.Upright(npc.Position, npc.Rotation.Z, npc.Scale);

    private static NpcSizeCounts CountSizes(IReadOnlyCollection<NpcBodySet> bodies, int withoutNpc) => new(
        bodies.Count,
        bodies.Count(body => body.Source == NpcSizeSource.BodyMesh),
        bodies.Count(body => body.Source == NpcSizeSource.ObjectBounds),
        bodies.Count(body => body.Source == NpcSizeSource.HumanoidApproximation),
        bodies.Count(body => body.Source == NpcSizeSource.Point),
        withoutNpc);
}
