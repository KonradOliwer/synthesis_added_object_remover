using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Decides which changes the cleaned mod made to other mods' objects, cells, terrain and navmeshes go back where it was removed from a wide area, keeping
/// every part a later plugin changed and everything that stays. It removes nothing and does not write.
/// </summary>
public static class WhereRemovedRestorer
{
    /// <param name="final">The decisions with every round and every move.</param>
    /// <param name="otherModsObjects">What became of other mods' objects left without support.</param>
    /// <param name="visibleTargets">The visible targets where the rounds measure them.</param>
    /// <param name="indexObjects">The object caches over a world of what stays that the step builds; asked once, when a placed record's area is cleared.</param>
    /// <param name="tooClose">The too-close rule with the given settings over such a world and its caches, in which the given objects cause no removals.</param>
    /// <param name="findHoldersOfObjectsThatStay">
    /// Given objects where they stand now, their index, which of them move away, the visible objects of such a world and the copies
    /// of the moving ones among those: each moving one that an object that stays rests on, with the first such object in id order, by
    /// the test that finds the objects left without support.
    /// </param>
    /// <param name="support">The settings of the test behind <paramref name="findHoldersOfObjectsThatStay"/>, whose touch gap bounds how far it looks.</param>
    /// <param name="findLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
    /// <param name="readLandHeights">The heights of a cell's LAND record at its version just below the cleaned mod's and at the winning one.</param>
    /// <param name="findNavmeshFaultIfWritten">The navmesh consistency check's first fault over the world the orders alone would leave; null when none.</param>
    /// <param name="settings">Null when the step is switched off.</param>
    /// <returns>
    /// Each candidate with what its area, the never-rules and the guards say, in record key order (LAND records in the order of their
    /// cells); a kind whose switch is off has none. <see cref="StepOutcome.SwitchedOff"/> when <paramref name="settings"/> is null.
    /// An unexpected error stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and nothing judged.
    /// </returns>
    public static RestoreWhereRemovedResult Plan(
        CollectedObjects collected,
        IRemovalDecisions final,
        OtherModsObjectsResult otherModsObjects,
        IVisibleTargetObjects visibleTargets,
        Func<CollectedObjects, IObjectCaches> indexObjects,
        Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> tooClose,
        Func<IReadOnlyList<TargetObject>, IVisibleTargetObjects, Func<TargetId, bool>, IVisibleObjectsOfAnyPlugin, IReadOnlySet<OtherId>, IReadOnlyDictionary<TargetId, OtherObject>>
            findHoldersOfObjectsThatStay,
        OtherModsObjectsSettings support,
        IBaseObjectShapes shapes,
        INavmeshes navmeshes,
        Func<RecordKey, RecordKey?> findLandWorldspace,
        Func<ExteriorCell, LandHeights> readLandHeights,
        Func<IReadOnlyList<WriteOrder>, NavmeshFault?> findNavmeshFaultIfWritten,
        LoadOrderPlugins plugins,
        PluginName cleanedMod,
        Execution execution,
        WhereRemovedRestoreSettings? settings)
    {
        if (settings is null) return RestoreWhereRemovedResult.SwitchedOff;

        var inputs = new StepInputs(
            collected, final, otherModsObjects, visibleTargets, indexObjects, tooClose, findHoldersOfObjectsThatStay, support.TouchGap, shapes, navmeshes,
            findLandWorldspace, readLandHeights, findNavmeshFaultIfWritten, plugins, cleanedMod, execution, settings);
        return Failures.Attempt(
            () => PlanEverything(inputs),
            failure => new RestoreWhereRemovedResult(
                StepOutcome.StoppedByUnexpectedError,
                Objects: [],
                Cells: [],
                Lands: [],
                Navmeshes: [],
                NavmeshGroups: [],
                Passes: 0,
                DoorsLeftRemoved: [],
                Failures: [PartFailure.OfWholePart(RestoreWhereRemovedParts.Step, failure)]));
    }

    private static RestoreWhereRemovedResult PlanEverything(StepInputs inputs)
    {
        var plans = BuildPlans(inputs);
        var (state, passes) = BuildLoop(inputs, plans).Run(plans.Start);
        return ResultOf(plans, state, passes, inputs.Settings);
    }

    /// <summary>The candidates judged by the never-rules and their areas, and every kind's plan before the guards between the kinds.</summary>
    private static Plans BuildPlans(StepInputs inputs)
    {
        var (collected, settings, execution) = (inputs.Collected, inputs.Settings, inputs.Execution);
        var spacesOnLand = SpacesOnLand(collected, inputs.FindLandWorldspace);
        var areas = new ClearedAreaJudge(collected.Targets, inputs.VisibleTargets, inputs.Final, collected.Spaces, settings.ClearedArea);
        var judge = new ChangeJudge(collected.Targets, areas, inputs.Plugins, spacesOnLand);
        var candidates = new RestoreCandidates(collected, inputs.Final, inputs.OtherModsObjects);
        var placed = settings.OtherModsObjects ? candidates.PlacedRecords() : [];
        var cells = settings.Cells ? candidates.Cells() : [];
        var placedJudged = ParallelMap.Run(execution, placed.Length, index => judge.Placed(placed[index]), ParallelMap.AutomaticRangeSize);
        ImmutableArray<CellChangeJudged> cellsJudged = [.. ParallelMap.Run(execution, cells.Length, index => judge.Cell(cells[index]), ParallelMap.AutomaticRangeSize)];
        List<RecordChangedByCleanedMod> cleared = [.. placedJudged.Where(change => change.IsRestored).Select(change => change.Change)];
        var removedByRun = RemovedByRun(collected, inputs.Final, inputs.OtherModsObjects);
        var presence = FinalPresence.Of(collected, removedByRun, cleared);
        var doors = new DoorLinks(collected.ChangedRecords, cleared);
        var ground = new GroundUnderObjects(inputs.Shapes, inputs.FindLandWorldspace);
        var landHeights = LandHeightsOf(collected, inputs.ReadLandHeights);
        var targetsThatMayStay = TargetsThatMayStay(collected, inputs.Final, cleared);
        var lands = candidates.Lands();
        var terrain = settings.Terrain
            ? PlanTerrain(lands, judge, targetsThatMayStay, presence, ground, landHeights, settings, execution)
            : new TerrainUndoPlan([.. lands.Select(land => new LandAreasJudged(land, Never: null, Quarters: []))], landHeights, settings.HeightChangeTolerance, settings.TerrainEdgeBlendDistance);
        var navmeshPlan = settings.Navmeshes ? PlanNavmeshes(inputs, judge, removedByRun, presence, doors) : null;
        var water = new WaterUndoPlan(cellsJudged, collected.ChangedCells, settings.WaterHeightChangeTolerance);
        return new Plans(
            placedJudged,
            cleared,
            presence,
            doors,
            spacesOnLand,
            ground,
            landHeights,
            targetsThatMayStay,
            new RestoreState([.. cleared.Select(_ => (PlacedRefusal?)null)], navmeshPlan, terrain, water, CausesLeftOut: null));
    }

    /// <summary>The guards between the kinds over the after-removal world, with the placed guards only when a placed record's area is cleared.</summary>
    private static RestoreLoop BuildLoop(StepInputs inputs, Plans plans)
    {
        var (collected, settings, shapes) = (inputs.Collected, inputs.Settings, inputs.Shapes);
        var reach = new GuardReach(shapes, settings.TooClose, inputs.TouchGap);
        var world = AfterRemovalWorld.Of(collected, inputs.Final, inputs.OtherModsObjects, inputs.CleanedMod, plans.Cleared, reach);
        var blocking = new ObjectsThatBlockActors(shapes, collected.WinningPrimitives);
        var landHeights = plans.LandHeights;
        var terrainGuards = settings.Terrain
            ? new TerrainGuards(() => OtherPluginsObjectsThatStay(world, collected, plans.Cleared), plans.Ground, plans.SpacesOnLand, cell => landHeights(cell).Current, settings.HeightChangeTolerance, inputs.Execution)
            : null;
        return new RestoreLoop(
            plans.Cleared.Count == 0 ? null : BuildPlacedGuards(inputs, plans, world, reach, blocking),
            terrainGuards,
            new RestoreLoopInputs(
                world,
                new StandingObjects(world, blocking, plans.FinalPresence),
                plans.TargetsThatMayStay,
                plans.Ground,
                inputs.FindLandWorldspace,
                (plan, cell) => landHeights(cell) with { Current = plan.PlannedHeightsOf(cell) ?? landHeights(cell).Current },
                settings.HeightChangeTolerance,
                inputs.Navmeshes,
                EveryNavmeshWithChangedTriangles(collected),
                ChangedNavmeshTriangles(collected),
                plans.FinalPresence,
                plans.Doors));
    }

    private static PlacedGuards BuildPlacedGuards(StepInputs inputs, Plans plans, AfterRemovalWorld world, GuardReach reach, ObjectsThatBlockActors blocking)
    {
        var caches = inputs.IndexObjects(world.World);
        var tooCloseSettings = inputs.Settings.TooClose;
        return new PlacedGuards(
            world,
            new PlacedGuardInputs(
                caches,
                excluded => inputs.TooClose(world.World, caches, excluded, tooCloseSettings),
                inputs.FindHoldersOfObjectsThatStay,
                inputs.Shapes,
                blocking,
                inputs.Navmeshes,
                inputs.FindLandWorldspace,
                inputs.Settings.ChangedTerrain),
            reach,
            plans.FinalPresence,
            new DoorLinkGuard(plans.Cleared, inputs.Collected.ChangedNavmeshes, inputs.FindNavmeshFaultIfWritten),
            inputs.Execution);
    }

    /// <summary>What every candidate became once the guards between the kinds change nothing more.</summary>
    private static RestoreWhereRemovedResult ResultOf(Plans plans, RestoreState state, int passes, WhereRemovedRestoreSettings settings)
    {
        var objects = WithRefusals(plans.PlacedJudged, plans.Cleared, state.Placed);
        return new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            objects,
            state.Water.Judged(),
            settings.Terrain ? state.Terrain.Judged() : [],
            state.Navmeshes?.Navmeshes() ?? [],
            state.Navmeshes?.Groups() ?? [],
            passes,
            plans.FinalPresence.DoorsEndingRemoved(index => state.Placed[index] is null),
            FailedChecks(objects));
    }

    /// <summary>The placed records whose check stopped, which stay as the cleaned mod changed them, in the order of <paramref name="objects"/>.</summary>
    private static ImmutableArray<PartFailure> FailedChecks(ImmutableArray<PlacedChangeJudged> objects) =>
    [
        .. objects
            .Select((judged, position) => (Judged: judged, Position: position))
            .Where(entry => entry.Judged.Refused?.Failure is not null)
            .Select(entry => new PartFailure(
                RestoreWhereRemovedParts.ObjectCheck,
                entry.Judged.Refused!.Failure,
                new FailureSubject(entry.Judged.Change.Key.ToString(), entry.Judged.Change.EditorId, entry.Position))),
    ];

    /// <summary>Each placed record judged, with the first refusal of each whose areas let it go back.</summary>
    /// <param name="cleared">The records whose areas let them go back, in key order.</param>
    /// <param name="refusals">By their index among <paramref name="cleared"/>.</param>
    private static ImmutableArray<PlacedChangeJudged> WithRefusals(PlacedChangeJudged[] judged, List<RecordChangedByCleanedMod> cleared, ImmutableArray<PlacedRefusal?> refusals)
    {
        var refusalOf = cleared.Zip(refusals).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First.Key, pair => pair.Second!);
        return [.. judged.Select(change => refusalOf.TryGetValue(change.Change.Key, out var refused) ? change with { Refused = refused, IsRestored = false } : change)];
    }

    /// <summary>The objects of other plugins that stay where they stand: the after-removal world's but the targets and the placed records whose areas let them go back.</summary>
    private static List<OtherObject> OtherPluginsObjectsThatStay(AfterRemovalWorld world, CollectedObjects collected, List<RecordChangedByCleanedMod> cleared)
    {
        var leftOut = collected.Targets.Select(target => target.Key).Concat(cleared.Select(change => change.Key)).ToHashSet();
        return [.. world.Causes.Concat(world.SupportOnly).Where(other => !leftOut.Contains(other.Key)).OrderBy(other => other.Key, RecordKeyOrder.Comparer)];
    }

    /// <summary>
    /// The triangles of every navmesh the cleaned mod changed or added at its winning version, each with the cell holding that version,
    /// in key order of the navmeshes.
    /// </summary>
    private static ImmutableArray<NavmeshTriangleInCell> ChangedNavmeshTriangles(CollectedObjects collected) =>
    [
        .. collected.ChangedNavmeshes
            .Where(navmesh => !navmesh.Winner.IsDeleted)
            .OrderBy(navmesh => navmesh.Navmesh, RecordKeyOrder.Comparer)
            .SelectMany(navmesh => navmesh.Winner.WorldTriangles().Select(triangle => new NavmeshTriangleInCell(navmesh.Navmesh, navmesh.Winner.Cell, triangle))),
    ];

    /// <summary>
    /// Every navmesh the cleaned mod changed or added, judged and grouped, with the groups that fail a guard on the facts read refused.
    /// A door stands for these guards, and its link is as it ends, when every placed record whose areas let it go back goes back.
    /// </summary>
    private static NavmeshUndoPlan PlanNavmeshes(StepInputs inputs, ChangeJudge judge, IReadOnlySet<RecordKey> removedByRun, FinalPresence presence, DoorLinks doors)
    {
        var collected = inputs.Collected;
        var changed = collected.ChangedNavmeshes.OrderBy(navmesh => navmesh.Navmesh, RecordKeyOrder.Comparer).ToList();
        var judged = ParallelMap.Run(inputs.Execution, changed.Count, index => judge.Navmesh(changed[index]), ParallelMap.AutomaticRangeSize);
        var byKey = changed.ToDictionary(navmesh => navmesh.Navmesh);
        var records = collected.ChangedRecords.ToDictionary(record => record.Key);
        var doorsIfAllGoBack = new DoorsAsTheyEnd(presence, doors, _ => true);
        return new NavmeshUndoPlan(
            judged,
            sunk => new NavmeshGroupGuards(byKey, sunk, records, removedByRun.Contains, doors, doorsIfAllGoBack, inputs.Plugins, inputs.CleanedMod),
            inputs.FindNavmeshFaultIfWritten);
    }

    /// <summary>The placed records a round or the other mods' objects step remove.</summary>
    private static HashSet<RecordKey> RemovedByRun(CollectedObjects collected, IRemovalDecisions final, OtherModsObjectsResult otherModsObjects) =>
        collected.Targets.Where(target => final.IsRemoved(target.Id)).Select(target => target.Key)
            .Concat(otherModsObjects.Objects.Where(outcome => outcome.IsRemoved).Select(outcome => outcome.Object.Key))
            .ToHashSet();

    /// <summary>
    /// Each cell's heights before the cleaned mod and at the winning version: for a LAND record the cleaned mod changed, those it
    /// carries; for any other, and one that carries none, its versions read, each cell once.
    /// </summary>
    private static Func<ExteriorCell, LandHeights> LandHeightsOf(CollectedObjects collected, Func<ExteriorCell, LandHeights> readLandHeights)
    {
        var changed = new Dictionary<ExteriorCell, LandHeights>();
        foreach (var land in collected.ChangedLands)
        {
            if (land.Heights is { } heights) changed.TryAdd(land.Cell, heights);
        }
        var read = new ComputedOncePerKey<ExteriorCell, LandHeights>(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);
        return cell => changed.TryGetValue(cell, out var heights) ? heights : read.Get(cell, () => readLandHeights(cell));
    }

    /// <summary>Per worldspace holding land, the spaces holding a target whose ground it is, in key order.</summary>
    private static Dictionary<RecordKey, ImmutableArray<RecordKey>> SpacesOnLand(CollectedObjects collected, Func<RecordKey, RecordKey?> findLandWorldspace) =>
        collected.TargetSpaces()
            .Select(space => (Space: space, Land: findLandWorldspace(space)))
            .Where(pair => pair.Land is not null)
            .GroupBy(pair => pair.Land!.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Space).Order(RecordKeyOrder.Comparer).ToImmutableArray());

    /// <summary>
    /// The LAND records with their quarters judged, planned with the ground under every target that stands where it stands last
    /// protected where it reaches the ground (G1). A target stands when it ends present with every placed record whose areas let it
    /// go back going back; the loop protects the ground under the others once a refusal lets them stand.
    /// </summary>
    /// <param name="lands">In the order of their cells.</param>
    private static TerrainUndoPlan PlanTerrain(
        ImmutableArray<LandChangedByCleanedMod> lands,
        ChangeJudge judge,
        IReadOnlyList<TargetObject> targetsThatMayStay,
        FinalPresence presence,
        GroundUnderObjects ground,
        Func<ExteriorCell, LandHeights> landHeights,
        WhereRemovedRestoreSettings settings,
        Execution execution)
    {
        var judged = ParallelMap.Run(execution, lands.Length, index => judge.Land(lands[index]), ParallelMap.AutomaticRangeSize);
        var plan = new TerrainUndoPlan(judged, landHeights, settings.HeightChangeTolerance, settings.TerrainEdgeBlendDistance);
        if (lands.IsEmpty) return plan;

        var standing = targetsThatMayStay.Where(target => !presence.EndsRemoved(target.Key, _ => true));
        return plan.Protect(TerrainKeptReason.UnderObjectThatStays, ground.UnderWhereTheyReachTheGround(standing, plan.CurrentHeightsOf));
    }

    /// <summary>Each target that is not removed, where it stands last, but the placed records whose areas let them go back.</summary>
    /// <param name="cleared">The placed records whose areas let them go back.</param>
    private static List<TargetObject> TargetsThatMayStay(CollectedObjects collected, IRemovalDecisions final, List<RecordChangedByCleanedMod> cleared)
    {
        var goingBack = cleared.Select(change => change.Key).ToHashSet();
        return [.. AfterRemovalWorld.TargetsWhereTheyStandLast(collected.Targets, final).Where(target => !goingBack.Contains(target.Key))];
    }

    /// <summary>Every navmesh whose triangles the cleaned mod changed or added, which all stay changed while the navmeshes are switched off.</summary>
    private static ImmutableArray<RecordKey> EveryNavmeshWithChangedTriangles(CollectedObjects collected) =>
        collected.ChangedNavmeshes.Where(navmesh => navmesh.ChangesTriangles).Select(navmesh => navmesh.Navmesh).Order(RecordKeyOrder.Comparer).ToImmutableArray();

    /// <summary>What <see cref="Plan"/> was given, with the step switched on.</summary>
    private sealed record StepInputs(
        CollectedObjects Collected,
        IRemovalDecisions Final,
        OtherModsObjectsResult OtherModsObjects,
        IVisibleTargetObjects VisibleTargets,
        Func<CollectedObjects, IObjectCaches> IndexObjects,
        Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> TooClose,
        Func<IReadOnlyList<TargetObject>, IVisibleTargetObjects, Func<TargetId, bool>, IVisibleObjectsOfAnyPlugin, IReadOnlySet<OtherId>, IReadOnlyDictionary<TargetId, OtherObject>>
            FindHoldersOfObjectsThatStay,
        float TouchGap,
        IBaseObjectShapes Shapes,
        INavmeshes Navmeshes,
        Func<RecordKey, RecordKey?> FindLandWorldspace,
        Func<ExteriorCell, LandHeights> ReadLandHeights,
        Func<IReadOnlyList<WriteOrder>, NavmeshFault?> FindNavmeshFaultIfWritten,
        LoadOrderPlugins Plugins,
        PluginName CleanedMod,
        Execution Execution,
        WhereRemovedRestoreSettings Settings);

    /// <summary>The candidates judged and every kind's plan before the guards between the kinds.</summary>
    /// <param name="PlacedJudged">Every placed record judged by the never-rules and its areas, in key order.</param>
    /// <param name="Cleared">The placed records whose areas let them go back, in key order, each with the parts its restore writes.</param>
    /// <param name="Start">The state before the first pass.</param>
    private sealed record Plans(
        PlacedChangeJudged[] PlacedJudged,
        List<RecordChangedByCleanedMod> Cleared,
        FinalPresence FinalPresence,
        DoorLinks Doors,
        Dictionary<RecordKey, ImmutableArray<RecordKey>> SpacesOnLand,
        GroundUnderObjects Ground,
        Func<ExteriorCell, LandHeights> LandHeights,
        List<TargetObject> TargetsThatMayStay,
        RestoreState Start);
}
