using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// Finds the target objects too close to another mod's object, by a multiple of their size and/or by a distance in game units, and proposes their removal. It also holds the NPC
/// body rules that the caches of NPC bodies (C8) and of placed NPCs (C10) apply, because the body of an NPC
/// is this step's rule.
/// </summary>
public static class TooCloseObjects
{
    /// <summary>The rules the NPC body cache (C8) applies.</summary>
    public static NpcBodyRules NpcBodyRules { get; } = new(NpcTemplateFlag.Traits, CreateOwnBodyResolver, DistinctBodies);

    private static Func<NpcTraits, NpcBody> CreateOwnBodyResolver(IMeshFiles meshFiles, IBodyMeshBounds bodyBounds) =>
        new NpcBodyResolver(meshFiles, bodyBounds).ResolveOwnBody;

    /// <summary>Bodies with the same box are tested once; the first of them is kept.</summary>
    private static NpcBodySet DistinctBodies(IReadOnlyList<NpcBody> possibleBodies)
    {
        var distinct = KeyedGroups.DistinctBy(possibleBodies, body => body.LocalBox, EqualityComparer<Box>.Default);
        return new NpcBodySet(distinct, Box.UnionAll(distinct.Select(body => body.LocalBox)));
    }

    /// <summary>The rules the placed-NPC index (C10) applies.</summary>
    public static PlacedNpcRules PlacedNpcRules { get; } = new(IsPlacedNpc, BodyTransform, CountSizes);

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

    /// <param name="placedNpcs">C10; the step leaves out the NPCs <paramref name="replacements"/> replaces.</param>
    public static TooCloseResult Find(
        CollectedObjects world,
        IVisibleTargetObjects visibleTargets,
        IObjectsThatCanCauseRemovals otherModObjects,
        IPlacedNpcsBySpace placedNpcs,
        Replacements replacements,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        Execution execution,
        TooCloseSettings settings) =>
        Find(
            new TooCloseInput(
                world.Targets, visibleTargets, world.Order, otherModObjects, new NpcsThatCanSpawn(placedNpcs, replacements), shapes, triangles, execution),
            settings);

    /// <remarks>When no removal zone is on, the step looks at nothing, not even for stuck NPCs, and the result is <see cref="StepOutcome.SwitchedOff"/>.</remarks>
    internal static TooCloseResult Find(TooCloseInput input, TooCloseSettings settings)
    {
        var zones = ZoneGrowth.Active(settings);
        if (zones.IsEmpty) return SwitchedOff(settings);

        var npcRule = NpcTooCloseRule.Create(
            settings.Npcs,
            onlyWithBlockingCollision => NpcStuckSearch.Create(
                input.Targets,
                input.VisibleTargets,
                input.Npcs,
                input.Shapes,
                input.Triangles,
                onlyWithBlockingCollision));
        var (search, largeOtherObjects) = settings.Zone switch
        {
            ZoneShape.BoundingBox => (FindInBoxZone(input, zones, npcRule), 0),
            ZoneShape.ObjectShape => FindInShapeZone(input, zones, npcRule),
            _ => throw new UnreachableException($"Unknown removal zone {settings.Zone}."),
        };
        return new TooCloseResult(
            StepOutcome.Performed,
            [.. search.Hits],
            [.. search.Hits.Select(hit => new ProposedRemoval(new TargetId(hit.TargetIndex), new RemovalReason.TooClose(hit.TooCloseTo.Id, hit.Cause)))],
            search.Work,
            npcRule.StuckSearch?.GetSummary(search.Work.Npcs),
            largeOtherObjects,
            [.. search.Failures],
            [.. zones.Select(zone => zone.Cause)],
            settings.Zone,
            settings.Npcs);
    }

    private static TooCloseResult SwitchedOff(TooCloseSettings settings) =>
        new(StepOutcome.SwitchedOff, [], [], TooCloseWork.Zero, Npc: null, LargeOtherObjects: 0, [], ZonesTested: [], settings.Zone, settings.Npcs);

    private static TooCloseSearchResult FindInBoxZone(TooCloseInput input, ImmutableArray<ZoneGrowth> zones, NpcTooCloseRule npcRule) =>
        TooCloseSearch.FindTooCloseTargets(
            input.Targets, input.OtherModObjects, input.Shapes, zones, npcRule, input.Order, input.Exec);

    private static (TooCloseSearchResult Result, int LargeOtherObjects) FindInShapeZone(TooCloseInput input, ImmutableArray<ZoneGrowth> zones, NpcTooCloseRule npcRule)
    {
        var search = ShapeZoneSearch.Create(
            input.Targets, input.VisibleTargets, input.OtherModObjects, input.Shapes, input.Triangles, zones, npcRule);
        var result = search.FindTooCloseTargets(input.Order, input.Exec);
        return (result, search.LargeOtherObjects);
    }
}
