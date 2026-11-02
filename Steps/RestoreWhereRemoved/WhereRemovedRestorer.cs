using System.Collections.Immutable;
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
    /// <summary>What the earlier steps found and decided.</summary>
    /// <param name="Final">The decisions with every round and every move.</param>
    /// <param name="OtherModsObjects">What became of other mods' objects left without support.</param>
    public sealed record EarlierResults(CollectedObjects Collected, IRemovalDecisions Final, OtherModsObjectsResult OtherModsObjects);

    /// <summary>What the guards ask about a placed record, a navmesh or a door going back.</summary>
    /// <param name="WorldCaches">The object caches over the world of what stays that the step builds; asked when a placed record's area is cleared.</param>
    /// <param name="TooClose">The too-close rule over such a world.</param>
    /// <param name="HoldersOfObjectsThatStay">The test that finds the objects left without support, asked in such a world.</param>
    /// <param name="NavmeshCheck">The navmesh check of a trial write.</param>
    public sealed record GuardRules(
        IObjectCachesOfWorlds WorldCaches, ITooCloseToWhatStaysRule TooClose, IHoldersOfObjectsThatStay HoldersOfObjectsThatStay, INavmeshCheckIfWritten NavmeshCheck);

    /// <summary>Where the step reads the load order.</summary>
    /// <param name="Caches">Index the visible targets where the rounds measure them; their shapes and navmeshes are read.</param>
    /// <param name="Plugin">Where the land a space uses and the heights of a cell's LAND record are read.</param>
    public sealed record LoadOrderReads(IRunCaches Caches, IPluginRecords Plugin, LoadOrderPlugins Plugins, PluginName CleanedMod);

    /// <param name="Settings">The step is switched off when none of its kinds is on.</param>
    public sealed record Input(EarlierResults Earlier, GuardRules Guards, LoadOrderReads Reads, Execution Execution, WhereRemovedRestoreSettings Settings);

    /// <summary>The part the measurement failures of the objects that stay, in the worlds the guards judge in, are recorded under.</summary>
    public static PartKey MeasurementFailurePart() => RestoreWhereRemovedParts.ObjectThatStays;

    /// <returns>
    /// Each candidate with what its area, the never-rules and the guards say, in record key order (LAND records in the order of their
    /// cells); a kind whose switch is off has none. <see cref="StepOutcome.SwitchedOff"/> when no kind is on.
    /// An unexpected error stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and nothing judged.
    /// </returns>
    public static RestoreWhereRemovedResult Plan(Input input)
    {
        var settings = input.Settings;
        if (!settings.Runs) return RestoreWhereRemovedResult.SwitchedOff;

        var inputs = new StepInputs(input, settings);
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
        var (input, earlier) = (inputs.Input, inputs.Input.Earlier);
        var (collected, final, settings, execution) = (earlier.Collected, earlier.Final, inputs.Settings, input.Execution);
        var spacesOnLand = LandInputs.SpacesOnLand(collected, inputs.FindLandWorldspace);
        var visibleTargets = input.Reads.Caches.IndexVisibleTargets(final.TargetsAsTheRoundsMeasureThem(collected.Targets));
        var areas = new ClearedAreaJudge(collected.Targets, visibleTargets, final, collected.Spaces, settings.ClearedArea);
        var judge = new ChangeJudge(collected.Targets, areas, input.Reads.Plugins, spacesOnLand);
        var candidates = new RestoreCandidates(collected, final, earlier.OtherModsObjects);
        var placed = settings.OtherModsObjects ? candidates.PlacedRecords() : [];
        var cells = settings.Cells ? candidates.Cells() : [];
        var placedJudged = ParallelMap.Run(execution, placed.Length, index => judge.Placed(placed[index]), ParallelMap.AutomaticRangeSize);
        ImmutableArray<CellChangeJudged> cellsJudged = [.. ParallelMap.Run(execution, cells.Length, index => judge.Cell(cells[index]), ParallelMap.AutomaticRangeSize)];
        List<RecordChangedByCleanedMod> cleared = [.. placedJudged.Where(change => change.IsRestored).Select(change => change.Change)];
        var removedByRun = ObjectsOfTheRun.RemovedByRun(collected, final, earlier.OtherModsObjects);
        var presence = FinalPresence.Of(collected, removedByRun, cleared);
        var doors = new DoorLinks(collected.ChangedRecords, cleared);
        var ground = new GroundUnderObjects(input.Reads.Caches.Shapes, inputs.FindLandWorldspace);
        var landHeights = LandInputs.LandHeightsOf(collected, inputs.ReadLandHeights);
        var targetsThatMayStay = ObjectsOfTheRun.TargetsThatMayStay(collected, final, cleared);
        var lands = candidates.Lands();
        var terrain = settings.Terrain
            ? PlanTerrain(lands, judge, targetsThatMayStay, presence, ground, landHeights, settings, execution)
            : new TerrainUndoPlan([.. lands.Select(land => new LandAreasJudged(land, Never: null, Quarters: []))], landHeights, settings.HeightChangeTolerance, settings.TerrainEdgeBlendDistance);
        var navmeshPlan = settings.Navmeshes ? PlanNavmeshes(input, judge, removedByRun, presence, doors) : null;
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
        var (input, earlier) = (inputs.Input, inputs.Input.Earlier);
        var (collected, settings, shapes) = (earlier.Collected, inputs.Settings, input.Reads.Caches.Shapes);
        var reach = new GuardReach(shapes, settings.TooClose, input.Guards.HoldersOfObjectsThatStay.TouchGap);
        var world = AfterRemovalWorld.Of(collected, earlier.Final, earlier.OtherModsObjects, input.Reads.CleanedMod, plans.Cleared, reach);
        var blocking = new ObjectsThatBlockActors(shapes, collected.WinningPrimitives);
        var landHeights = plans.LandHeights;
        var terrainGuards = settings.Terrain
            ? new TerrainGuards(() => ObjectsOfTheRun.OtherPluginsObjectsThatStay(world, collected, plans.Cleared), plans.Ground, plans.SpacesOnLand, cell => landHeights(cell).Current, settings.HeightChangeTolerance, input.Execution)
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
                input.Reads.Caches.Navmeshes,
                NavmeshInputs.EveryNavmeshWithChangedTriangles(collected),
                NavmeshInputs.ChangedNavmeshTriangles(collected),
                plans.FinalPresence,
                plans.Doors));
    }

    private static PlacedGuards BuildPlacedGuards(StepInputs inputs, Plans plans, AfterRemovalWorld world, GuardReach reach, ObjectsThatBlockActors blocking)
    {
        var (input, guards, runCaches) = (inputs.Input, inputs.Input.Guards, inputs.Input.Reads.Caches);
        var caches = guards.WorldCaches.Of(world.World);
        var tooCloseSettings = inputs.Settings.TooClose;
        return new PlacedGuards(
            world,
            new PlacedGuardInputs(
                caches,
                leftOut => guards.TooClose.Create(world.World, leftOut, tooCloseSettings),
                guards.HoldersOfObjectsThatStay.Over(world.World, world.Now),
                runCaches.Shapes,
                blocking,
                runCaches.Navmeshes,
                inputs.FindLandWorldspace,
                inputs.Settings.ChangedTerrain),
            reach,
            plans.FinalPresence,
            new DoorLinkGuard(plans.Cleared, input.Earlier.Collected.ChangedNavmeshes, guards.NavmeshCheck.FindFaultIfWritten),
            input.Execution);
    }

    /// <summary>What every candidate became once the guards between the kinds change nothing more.</summary>
    private static RestoreWhereRemovedResult ResultOf(Plans plans, RestoreState state, int passes, WhereRemovedRestoreSettings settings)
    {
        var objects = PlacedJudgments.WithRefusals(plans.PlacedJudged, plans.Cleared, state.Placed);
        return new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            objects,
            state.Water.Judged(),
            settings.Terrain ? state.Terrain.Judged() : [],
            state.Navmeshes?.Navmeshes() ?? [],
            state.Navmeshes?.Groups() ?? [],
            passes,
            plans.FinalPresence.DoorsEndingRemoved(index => state.Placed[index] is null),
            PlacedJudgments.FailedChecks(objects));
    }

    /// <summary>
    /// Every navmesh the cleaned mod changed or added, judged and grouped, with the groups that fail a guard on the facts read refused.
    /// A door stands for these guards, and its link is as it ends, when every placed record whose areas let it go back goes back.
    /// </summary>
    private static NavmeshUndoPlan PlanNavmeshes(Input input, ChangeJudge judge, IReadOnlySet<RecordKey> removedByRun, FinalPresence presence, DoorLinks doors)
    {
        var (collected, reads) = (input.Earlier.Collected, input.Reads);
        var changed = collected.ChangedNavmeshes.OrderBy(navmesh => navmesh.Navmesh, RecordKeyOrder.Comparer).ToList();
        var judged = ParallelMap.Run(input.Execution, changed.Count, index => judge.Navmesh(changed[index]), ParallelMap.AutomaticRangeSize);
        var byKey = changed.ToDictionary(navmesh => navmesh.Navmesh);
        var records = collected.ChangedRecords.ToDictionary(record => record.Key);
        var doorsIfAllGoBack = new DoorsAsTheyEnd(presence, doors, _ => true);
        return new NavmeshUndoPlan(
            judged,
            sunk => new NavmeshGroupGuards(byKey, sunk, records, removedByRun.Contains, doors, doorsIfAllGoBack, reads.Plugins, reads.CleanedMod),
            input.Guards.NavmeshCheck.FindFaultIfWritten);
    }

    /// <summary>
    /// The LAND records with their quarters judged, planned with the ground under every target that stands where it stands last
    /// protected where it reaches the ground. A target stands when it ends present with every placed record whose areas let it
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

    /// <summary>What <see cref="Plan"/> was given, with the step switched on.</summary>
    private sealed record StepInputs(Input Input, WhereRemovedRestoreSettings Settings)
    {
        /// <returns>The worldspace whose land the space uses; null for a space without land.</returns>
        public RecordKey? FindLandWorldspace(RecordKey space) => Input.Reads.Plugin.FindLandWorldspace(space);

        /// <summary>The heights of the cell's LAND record at its version just below the cleaned mod's and at the winning one.</summary>
        public LandHeights ReadLandHeights(ExteriorCell cell) => new(Input.Reads.Plugin.ReadTerrainBelow(Input.Reads.CleanedMod, cell), Input.Reads.Plugin.ReadTerrain(cell));
    }

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
