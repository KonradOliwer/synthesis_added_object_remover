using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>
/// Creates the rule of target objects too close to another mod's object, by a multiple of their size and/or by a distance in game
/// units, or stuck with another mod's NPC.
/// </summary>
public static class TooCloseRules
{
    /// <summary>The NPC cache rules, which the NPC body cache and the placed-NPC index apply (see <see cref="NpcCacheRules"/>).</summary>
    public static (NpcBodyRules Bodies, PlacedNpcRules PlacedNpcs) CreateNpcCacheRules() => (NpcCacheRules.Bodies, NpcCacheRules.PlacedNpcs);

    /// <summary>The searches over all of the world's target objects, one for each set of settings asked.</summary>
    public static ITooCloseSearches CreateSearches(TooCloseWorld world) => new TooCloseSearches(world, NpcsOf(world));

    /// <summary>The check of one target object at a given placement; it finds nothing when no removal zone is on.</summary>
    public static IPlacementRule CreatePlacementRule(TooCloseWorld world, TooCloseSettings settings)
    {
        var zones = ZoneGrowth.Active(settings);
        return new TooClosePlacementRule(
            zones.IsEmpty ? null : new ComputedOnce<ITooCloseSearch>(() => CreateSearch(world, NpcsOf(world), settings, zones).Search));
    }

    /// <summary>The check of an earlier version at its old place (see <see cref="OldPlaceTooCloseRule"/>).</summary>
    /// <param name="targets">The run's target objects, measured where the cleaned mod put them.</param>
    /// <param name="caches">The run's caches: the shapes and the triangles, and the index of the run's target objects.</param>
    /// <param name="worldCaches">The object caches of the old-place worlds, built when the first earlier version is checked.</param>
    /// <param name="causes">The objects that can cause removals in those worlds, selected among <paramref name="worldCaches"/>.</param>
    public static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(
        IReadOnlyList<TargetObject> targets, IRunCaches caches, IObjectCachesOfWorlds worldCaches, ICausesOfWorlds causes, TooCloseSettings settings) =>
        new OldPlaceTooCloseRule(targets, new OtherWorlds(caches, worldCaches, causes), settings);

    /// <summary>The check of a placed record going back, among what stays (see <see cref="TooCloseToWhatStaysRule"/>).</summary>
    /// <param name="caches">The run's caches: the shapes and the triangles.</param>
    /// <param name="worldCaches">The object caches of the worlds of what stays.</param>
    /// <param name="causes">The objects that can cause removals in those worlds, selected among <paramref name="worldCaches"/>.</param>
    public static ITooCloseToWhatStaysRule CreateTooCloseToWhatStaysRule(IRunCaches caches, IObjectCachesOfWorlds worldCaches, ICausesOfWorlds causes) =>
        new TooCloseToWhatStaysRule(new OtherWorlds(caches, worldCaches, causes));

    /// <summary>The NPCs that can spawn in the world: its placed NPCs but those its replacements replace.</summary>
    private static NpcsThatCanSpawn NpcsOf(TooCloseWorld world) => new(world.PlacedNpcs, world.Selected.Replacements);

    /// <summary>The search over the world with the zones that are on, and the NPC rule of the settings' NPC handling it uses.</summary>
    internal static (ITooCloseSearch Search, NpcTooCloseRule NpcRule) CreateSearch(
        TooCloseWorld world, INpcsThatCanSpawn npcs, TooCloseSettings settings, ImmutableArray<ZoneGrowth> zones)
    {
        var npcRule = NpcTooCloseRule.Create(
            settings.Npcs,
            onlyWithBlockingCollision => NpcStuckSearch.Create(world.VisibleTargets, npcs, world.Shapes, world.Triangles, onlyWithBlockingCollision));
        var (otherModObjects, zone) = (world.Selected.ViewFor(settings.Npcs), settings.Zone);
        ITooCloseSearch search = zone switch
        {
            ZoneShape.BoundingBox => new BoxZoneSearch(world.Targets, otherModObjects, world.Shapes, zones, npcRule),
            ZoneShape.ObjectShape => ShapeZoneSearch.Create(world.Targets, world.VisibleTargets, otherModObjects, world.Shapes, world.Triangles, zones, npcRule),
            _ => throw new UnreachableException($"Unknown removal zone {zone}."),
        };
        return (search, npcRule);
    }
}
