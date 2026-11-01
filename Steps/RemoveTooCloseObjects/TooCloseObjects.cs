using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// Finds the target objects too close to another mod's object, by a multiple of their size and/or by a distance in game units, and proposes their removal. It also holds the NPC
/// body rules that the caches of NPC bodies and of placed NPCs apply, because the body of an NPC
/// is this step's rule.
/// </summary>
public static class TooCloseObjects
{
    /// <summary>The rules the NPC body cache applies.</summary>
    public static NpcBodyRules NpcBodyRules { get; } = new(NpcTemplateFlag.Traits, CreateOwnBodyResolver, DistinctBodies);

    private static Func<NpcTraits, NpcBody> CreateOwnBodyResolver(IMeshFiles meshFiles, IBodyMeshBounds bodyBounds) =>
        new NpcBodyResolver(meshFiles, bodyBounds).ResolveOwnBody;

    /// <summary>Bodies with the same box are tested once; the first of them is kept.</summary>
    private static NpcBodySet DistinctBodies(IReadOnlyList<NpcBody> possibleBodies)
    {
        var distinct = KeyedGroups.DistinctBy(possibleBodies, body => body.LocalBox, EqualityComparer<Box>.Default);
        return new NpcBodySet(distinct, Box.UnionAll(distinct.Select(body => body.LocalBox)));
    }

    /// <summary>The rules the placed-NPC index applies.</summary>
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

    /// <param name="Selected">Each pass takes the objects that can cause removals for its own NPC handling.</param>
    /// <param name="PlacedNpcs">The step leaves out the NPCs the replacements in <paramref name="Selected"/> replace.</param>
    /// <param name="Decisions">The run's decisions so far, which the step's round is added to.</param>
    public sealed record Input(
        CollectedObjects World,
        IVisibleTargetObjects VisibleTargets,
        SelectedObjects Selected,
        IPlacedNpcsBySpace PlacedNpcs,
        IBaseObjectShapes Shapes,
        ITriangleMeshes Triangles,
        Execution Execution,
        IEnumerable<ObjectsJudgedWith<TooCloseSettings>> Groups,
        IRunDecisions Decisions);

    /// <summary>
    /// Runs once for each distinct set of settings over the objects judged with it, and proposes the removals of all of them in target order.
    /// </summary>
    public static Decided<TooCloseResult> Find(Input input)
    {
        var selected = input.Selected;
        var passInput = new TooCloseInput(
            input.World.Targets,
            input.VisibleTargets,
            input.World.Order,
            selected,
            new NpcsThatCanSpawn(input.PlacedNpcs, selected.Replacements),
            input.Shapes,
            input.Triangles,
            input.Execution);
        TooCloseResult result = new([.. ObjectsJudgedWith<TooCloseSettings>.MergeEqualSettings(input.Groups).Select(group => FindPass(passInput, group))]);
        return new Decided<TooCloseResult>(result, input.Decisions.Apply(RoundKind.TooClose, result.Proposals));
    }

    /// <param name="Selected">The rule takes the objects that can cause removals for the NPC handling of <paramref name="Settings"/>.</param>
    /// <param name="PlacedNpcs">The rule leaves out the NPCs the replacements in <paramref name="Selected"/> replace.</param>
    public sealed record PlacementRuleInput(
        IReadOnlyList<TargetObject> Targets,
        IVisibleTargetObjects VisibleTargets,
        SelectedObjects Selected,
        IPlacedNpcsBySpace PlacedNpcs,
        IBaseObjectShapes Shapes,
        ITriangleMeshes Triangles,
        TooCloseSettings Settings);

    /// <param name="Targets">The run's target objects, measured where the cleaned mod put them.</param>
    /// <param name="Caches">The run's caches: the index of <paramref name="Targets"/>, the shapes and the triangles.</param>
    /// <param name="WorldCaches">The object caches of the old-place world, built when the first earlier version is checked.</param>
    /// <param name="Causes">The objects that can cause removals in the old-place world, selected among <paramref name="WorldCaches"/>.</param>
    public sealed record OldPlaceRuleInput(
        IReadOnlyList<TargetObject> Targets, IRunCaches Caches, IObjectCachesOfWorlds WorldCaches, ICausesOfWorlds Causes, TooCloseSettings Settings);

    /// <param name="Caches">The run's caches: the shapes and the triangles.</param>
    /// <param name="WorldCaches">The object caches of the worlds of what stays.</param>
    /// <param name="Causes">The objects that can cause removals in those worlds, selected among <paramref name="WorldCaches"/>.</param>
    public sealed record WhatStaysRuleInput(IRunCaches Caches, IObjectCachesOfWorlds WorldCaches, ICausesOfWorlds Causes);

    /// <summary>The step's check of one target object at a given placement; it finds nothing when no removal zone is on.</summary>
    public static IPlacementRule CreatePlacementRule(PlacementRuleInput input)
    {
        var zones = ZoneGrowth.Active(input.Settings);
        return new TooClosePlacementRule(zones.IsEmpty ? null : new ComputedOnce<ITooCloseSearch>(() => CreateSearch(input, zones)));
    }

    /// <summary>The search of <see cref="CreatePlacementRule"/>, built when the rule first checks a placement.</summary>
    private static ITooCloseSearch CreateSearch(PlacementRuleInput input, ImmutableArray<ZoneGrowth> zones)
    {
        var (settings, selected) = (input.Settings, input.Selected);
        var npcRule = CreateNpcRule(input.VisibleTargets, new NpcsThatCanSpawn(input.PlacedNpcs, selected.Replacements), input.Shapes, input.Triangles, settings.Npcs);
        return CreateSearch(input.Targets, input.VisibleTargets, selected.ViewFor(settings.Npcs), input.Shapes, input.Triangles, zones, npcRule, settings.Zone);
    }

    /// <summary>The judge step's check of an earlier version at its old place (see <see cref="OldPlaceTooCloseRule"/>).</summary>
    public static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(OldPlaceRuleInput input) => new OldPlaceTooCloseRule(input);

    /// <summary>The restore step's check of a placed record going back, among what stays (see <see cref="TooCloseToWhatStaysRule"/>).</summary>
    public static ITooCloseToWhatStaysRule CreateTooCloseToWhatStaysRule(WhatStaysRuleInput input) => new TooCloseToWhatStaysRule(input);

    /// <summary>How the step words a too-close finding as the reason of a removal.</summary>
    internal static RemovalReason ReasonOf(ZoneHit hit) => new TooClose(hit.Other, hit.Cause);

    /// <remarks>When no removal zone is on, the pass looks at nothing, not even for stuck NPCs, and its outcome is <see cref="StepOutcome.SwitchedOff"/>.</remarks>
    private static TooClosePass FindPass(TooCloseInput input, ObjectsJudgedWith<TooCloseSettings> group)
    {
        var settings = group.Settings;
        var zones = ZoneGrowth.Active(settings);
        if (zones.IsEmpty) return SwitchedOff(group);

        var npcRule = CreateNpcRule(input.VisibleTargets, input.Npcs, input.Shapes, input.Triangles, settings.Npcs);
        var search = CreateSearch(
            input.Targets, input.VisibleTargets, input.Selected.ViewFor(settings.Npcs), input.Shapes, input.Triangles, zones, npcRule, settings.Zone);
        var found = search.FindTooCloseTargets(group.Objects, input.Order, input.Exec);
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

    private static NpcTooCloseRule CreateNpcRule(
        IVisibleTargetObjects visibleTargets, INpcsThatCanSpawn npcs, IBaseObjectShapes shapes, ITriangleMeshes triangles, NpcHandling handling) =>
        NpcTooCloseRule.Create(
            handling,
            onlyWithBlockingCollision => NpcStuckSearch.Create(visibleTargets, npcs, shapes, triangles, onlyWithBlockingCollision));

    private static ITooCloseSearch CreateSearch(
        IReadOnlyList<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        IObjectsThatCanCauseRemovals otherModObjects,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ImmutableArray<ZoneGrowth> zones,
        NpcTooCloseRule npcRule,
        ZoneShape zone) =>
        zone switch
        {
            ZoneShape.BoundingBox => new BoxZoneSearch(targets, otherModObjects, shapes, zones, npcRule),
            ZoneShape.ObjectShape => ShapeZoneSearch.Create(targets, visibleTargets, otherModObjects, shapes, triangles, zones, npcRule),
            _ => throw new UnreachableException($"Unknown removal zone {zone}."),
        };
}
