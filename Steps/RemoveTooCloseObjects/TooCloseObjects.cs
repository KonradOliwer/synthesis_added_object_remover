using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// Finds the target objects too close to another mod's object, by a multiple of their size and/or by a distance in game units, and
/// proposes their removal.
/// </summary>
public static class TooCloseObjects
{
    /// <summary>The objects a too-close check measures the target objects among.</summary>
    /// <param name="Selected">Each check takes the objects that can cause removals for its own NPC handling.</param>
    /// <param name="PlacedNpcs">The check leaves out the NPCs the replacements in <paramref name="Selected"/> replace.</param>
    public sealed record TooCloseWorld(
        IReadOnlyList<TargetObject> Targets,
        IVisibleTargetObjects VisibleTargets,
        SelectedObjects Selected,
        IPlacedNpcsBySpace PlacedNpcs,
        IBaseObjectShapes Shapes,
        ITriangleMeshes Triangles);

    /// <param name="Collected">The run's objects, among whose other-mod objects the step counts the ones it leaves out.</param>
    /// <param name="Order">The order the passes work through the target objects in.</param>
    /// <param name="Decisions">The run's decisions so far, which the step's round is added to.</param>
    public sealed record Input(
        TooCloseWorld World,
        CollectedObjects Collected,
        WorkOrder Order,
        Execution Execution,
        IEnumerable<ObjectsJudgedWith<TooCloseSettings>> Groups,
        IRunDecisions Decisions);

    /// <summary>
    /// Runs once for each distinct set of settings over the objects judged with it, and proposes the removals of all of them in target order.
    /// </summary>
    public static Decided<TooCloseResult> Find(Input input)
    {
        var npcs = NpcsOf(input.World);
        ImmutableArray<TooClosePass> passes = [.. ObjectsJudgedWith<TooCloseSettings>.MergeEqualSettings(input.Groups).Select(group => FindPass(input, npcs, group))];
        TooCloseResult result = new(passes, LeftOutOf(input, passes));
        return new Decided<TooCloseResult>(result, input.Decisions.Apply(RoundKind.TooClose, result.Proposals));
    }

    /// <summary>Nothing is left out when no pass performed.</summary>
    private static OtherObjectsLeftOut LeftOutOf(Input input, ImmutableArray<TooClosePass> passes) =>
        passes.Any(pass => pass.Outcome == StepOutcome.Performed)
            ? OtherObjectsLeftOutCounter.Take(input.Collected, input.World.Shapes, input.World.VisibleTargets)
            : new OtherObjectsLeftOut([], PlacedNpcs: 0);

    public sealed record PlacementRuleInput(TooCloseWorld World, TooCloseSettings Settings);

    /// <param name="Caches">The run's caches: the shapes and the triangles, and the index of the run's target objects.</param>
    /// <param name="WorldCaches">The object caches of the worlds asked about.</param>
    /// <param name="Causes">The objects that can cause removals in those worlds, selected among <paramref name="WorldCaches"/>.</param>
    public sealed record WorldRuleInput(IRunCaches Caches, IObjectCachesOfWorlds WorldCaches, ICausesOfWorlds Causes);

    /// <param name="Targets">The run's target objects, measured where the cleaned mod put them.</param>
    /// <param name="Worlds">The old-place worlds, whose caches are built when the first earlier version is checked.</param>
    public sealed record OldPlaceRuleInput(IReadOnlyList<TargetObject> Targets, WorldRuleInput Worlds, TooCloseSettings Settings);

    /// <summary>The NPC cache rules, which the NPC body cache and the placed-NPC index apply (see <see cref="NpcCacheRules"/>).</summary>
    public static (NpcBodyRules Bodies, PlacedNpcRules PlacedNpcs) CreateNpcCacheRules() => (NpcCacheRules.Bodies, NpcCacheRules.PlacedNpcs);

    /// <summary>The step's check of one target object at a given placement; it finds nothing when no removal zone is on.</summary>
    public static IPlacementRule CreatePlacementRule(PlacementRuleInput input)
    {
        var zones = ZoneGrowth.Active(input.Settings);
        return new TooClosePlacementRule(
            zones.IsEmpty ? null : new ComputedOnce<ITooCloseSearch>(() => CreateSearch(input.World, NpcsOf(input.World), input.Settings, zones).Search));
    }

    /// <summary>The check of an earlier version at its old place (see <see cref="OldPlaceTooCloseRule"/>).</summary>
    public static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(OldPlaceRuleInput input) => new OldPlaceTooCloseRule(input);

    /// <summary>The check of a placed record going back, among what stays (see <see cref="TooCloseToWhatStaysRule"/>).</summary>
    public static ITooCloseToWhatStaysRule CreateTooCloseToWhatStaysRule(WorldRuleInput input) => new TooCloseToWhatStaysRule(input);

    /// <summary>How the step words a too-close finding as the reason of a removal.</summary>
    internal static RemovalReason ReasonOf(ZoneHit hit) => new TooClose(hit.Other, hit.Cause);

    /// <summary>The NPCs that can spawn in the world: its placed NPCs but those its replacements replace.</summary>
    private static NpcsThatCanSpawn NpcsOf(TooCloseWorld world) => new(world.PlacedNpcs, world.Selected.Replacements);

    /// <remarks>When no removal zone is on, the pass looks at nothing, not even for stuck NPCs, and its outcome is <see cref="StepOutcome.SwitchedOff"/>.</remarks>
    private static TooClosePass FindPass(Input input, INpcsThatCanSpawn npcs, ObjectsJudgedWith<TooCloseSettings> group)
    {
        var settings = group.Settings;
        var zones = ZoneGrowth.Active(settings);
        if (zones.IsEmpty) return SwitchedOff(group);

        var (search, npcRule) = CreateSearch(input.World, npcs, settings, zones);
        var found = search.FindTooCloseTargets(group.Objects, input.Order, input.Execution);
        return new TooClosePass(
            group.Objects,
            StepOutcome.Performed,
            [.. found.Hits],
            [.. found.Hits.Select(hit => new ProposedRemoval(new TargetId(hit.TargetIndex), ReasonOf(new ZoneHit(hit.TooCloseTo.Id, hit.Cause))))],
            found.Work,
            npcRule.StuckSearch?.GetSummary(found.Work.Npcs),
            search.LargeOtherObjects,
            [.. found.Failures],
            [.. zones.Select(zone => zone.Cause)],
            settings.Zone,
            settings.Npcs);
    }

    private static TooClosePass SwitchedOff(ObjectsJudgedWith<TooCloseSettings> group) =>
        new(group.Objects, StepOutcome.SwitchedOff, [], [], TooCloseWork.Zero, Npc: null, LargeOtherObjects: 0, [], ZonesTested: [], group.Settings.Zone, group.Settings.Npcs);

    /// <summary>The search over the world with the zones that are on, and the NPC rule of the settings' NPC handling it uses.</summary>
    private static (ITooCloseSearch Search, NpcTooCloseRule NpcRule) CreateSearch(
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
