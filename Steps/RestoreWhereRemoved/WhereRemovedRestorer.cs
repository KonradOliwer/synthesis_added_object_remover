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
/// every part a later plugin changed and everything that stays. Where an area goes back, the cleaned mod's objects still standing there
/// are removed, except those the never-rules of the other removal steps keep. It does not write.
/// </summary>
public static class WhereRemovedRestorer
{
    /// <summary>What the earlier steps found.</summary>
    /// <param name="OtherModsObjects">What became of other mods' objects left without support.</param>
    public sealed record EarlierResults(CollectedObjects Collected, OtherModsObjectsResult OtherModsObjects);

    /// <summary>What the guards ask about a placed record, a navmesh or a door going back.</summary>
    /// <param name="WorldCaches">The object caches over the world of what stays that the step builds; asked when a placed record's area is cleared.</param>
    /// <param name="TooClose">The too-close rule over such a world.</param>
    /// <param name="HoldersOfObjectsThatStay">The test that finds the objects left without support, asked in such a world.</param>
    /// <param name="NavmeshCheck">The navmesh check of a trial write.</param>
    public sealed record GuardRules(
        IObjectCachesOfWorlds WorldCaches, ITooCloseToWhatStaysRule TooClose, IHoldersOfObjectsThatStay HoldersOfObjectsThatStay, INavmeshCheckIfWritten NavmeshCheck);

    /// <summary>What the removal of the leftovers in restored areas asks of the other steps.</summary>
    /// <param name="VisibleKinds">The kinds the step for visible objects left behind always keeps.</param>
    /// <param name="InvisibleKinds">The kinds the step for invisible objects left behind always keeps.</param>
    /// <param name="OtherModsObjects">The step for other mods' objects left without support, asked once leftovers were removed.</param>
    public sealed record LeftoverRules(IKindsAlwaysKept VisibleKinds, IKindsAlwaysKept InvisibleKinds, IOtherModsObjectsAfterLeftovers OtherModsObjects);

    /// <summary>Where the step reads the load order.</summary>
    /// <param name="Caches">Index the shown targets where the rounds measure them; their shapes and navmeshes are read.</param>
    /// <param name="Plugin">Where the land a space uses and the heights and winning plugin of a cell's LAND record are read.</param>
    public sealed record LoadOrderReads(IRunCaches Caches, IPluginRecords Plugin, LoadOrderPlugins Plugins, PluginName CleanedMod);

    /// <param name="Decisions">The decisions with every round and every move; the leftovers' round and the rounds that follow it are added to them.</param>
    /// <param name="Settings">The step is switched off when none of its kinds is on.</param>
    /// <param name="TimeSinceStart">The run's clock, which times the parts of the step.</param>
    public sealed record Input(
        EarlierResults Earlier,
        IRunDecisions Decisions,
        GuardRules Guards,
        LeftoverRules Leftovers,
        LoadOrderReads Reads,
        Execution Execution,
        WhereRemovedRestoreSettings Settings,
        Func<TimeSpan> TimeSinceStart);

    /// <summary>The part the measurement failures of the objects that stay, in the worlds the guards judge in, are recorded under.</summary>
    public static PartKey MeasurementFailurePart() => RestoreWhereRemovedParts.ObjectThatStays;

    /// <returns>
    /// Each candidate with what its area, the never-rules and the guards say, in record key order (LAND records in the order of their
    /// cells); a kind whose switch is off has none. <see cref="StepOutcome.SwitchedOff"/> when no kind is on.
    /// An unexpected error stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure, nothing judged and nothing removed.
    /// The decisions hold the leftovers' round and the rounds that follow it.
    /// </returns>
    public static Decided<RestoreWhereRemovedResult> Plan(Input input)
    {
        var (result, rounds) = PlanUnlessSwitchedOff(input);
        return new Decided<RestoreWhereRemovedResult>(result, input.Decisions.ApplyRounds(rounds));
    }

    private static (RestoreWhereRemovedResult Result, ImmutableArray<ProposedRound> Rounds) PlanUnlessSwitchedOff(Input input)
    {
        var otherModsObjects = input.Earlier.OtherModsObjects;
        if (!input.Settings.Runs) return (RestoreWhereRemovedResult.SwitchedOff(otherModsObjects), []);

        return Failures.Attempt(
            () => PlanEverything(input),
            failure => (
                new RestoreWhereRemovedResult(
                    StepOutcome.StoppedByUnexpectedError,
                    Objects: [],
                    Cells: [],
                    Lands: [],
                    Navmeshes: [],
                    NavmeshGroups: [],
                    Passes: 0,
                    DoorsLeftRemoved: [],
                    Failures: [PartFailure.OfWholePart(RestoreWhereRemovedParts.Step, failure)],
                    Times: RestoreWhereRemovedTimes.None,
                    Leftovers: [],
                    otherModsObjects),
                ImmutableArray<ProposedRound>.Empty));
    }

    private static (RestoreWhereRemovedResult Result, ImmutableArray<ProposedRound> Rounds) PlanEverything(Input input)
    {
        var clock = input.TimeSinceStart;
        var ((inputs, plans), plansTime) = Timed.Run(clock, () => BuildPlans(input));
        var ((loop, worldTime), guardsTime) = Timed.Run(clock, () => BuildLoop(inputs, plans));
        var ((state, passes, placedGuardsTime), passesTime) = Timed.Run(clock, () => loop.Run(plans.Start));
        var times = new RestoreWhereRemovedTimes(plansTime, worldTime, guardsTime - worldTime, passesTime, placedGuardsTime);
        return (ResultOf(plans, state, passes, times, input.Settings), plans.Leftovers.Rounds);
    }

    /// <summary>
    /// The candidates judged by the never-rules and their areas, the leftovers in the cleared areas removed, and every kind's plan
    /// before the guards between the kinds, made with the leftovers gone.
    /// </summary>
    /// <returns>What the plans and the guards read, with the leftovers gone, and the plans.</returns>
    internal static (StepInputs Inputs, Plans Plans) BuildPlans(Input input)
    {
        var judged = new StepInputs(input, input.Decisions, input.Earlier.OtherModsObjects);
        var verdicts = JudgeAreas(judged);
        var leftovers = RemoveLeftovers(judged, verdicts);
        var afterLeftovers = new StepInputs(input, leftovers.Decisions, leftovers.OtherModsObjects);
        return (afterLeftovers, PlanKinds(afterLeftovers, verdicts, leftovers));
    }

    /// <summary>Every candidate judged by the never-rules and its areas; a kind whose switch is off has none.</summary>
    private static AreaVerdicts JudgeAreas(StepInputs inputs)
    {
        var (input, settings, final) = (inputs.Input, inputs.Settings, inputs.Final);
        var (collected, execution) = (input.Earlier.Collected, input.Execution);
        var spacesOnLand = LandInputs.SpacesOnLand(collected, inputs.FindLandWorldspace);
        var shownTargets = input.Reads.Caches.IndexShownTargets(final.TargetsAsTheRoundsMeasureThem(collected.Targets));
        var areas = new ClearedAreaJudge(collected.Targets, shownTargets, final, collected.Spaces, settings.ClearedArea);
        var judge = new ChangeJudge(collected.Targets, areas, input.Reads.Plugins, spacesOnLand);
        var candidates = new RestoreCandidates(collected, final, inputs.OtherModsObjects);
        var placed = settings.OtherModsObjects ? candidates.PlacedRecords() : [];
        var cells = settings.Cells ? candidates.Cells() : [];
        var lands = candidates.Lands();
        var navmeshes = settings.Navmeshes ? collected.ChangedNavmeshes.OrderBy(navmesh => navmesh.Navmesh, RecordKeyOrder.Comparer).ToList() : [];
        return new AreaVerdicts(
            ParallelMap.Run(execution, placed.Length, index => judge.Placed(placed[index]), ParallelMap.AutomaticRangeSize),
            [.. ParallelMap.Run(execution, cells.Length, index => judge.Cell(cells[index]), ParallelMap.AutomaticRangeSize)],
            lands,
            settings.Terrain ? ParallelMap.Run(execution, lands.Length, index => judge.Land(lands[index]), ParallelMap.AutomaticRangeSize) : [],
            navmeshes,
            ParallelMap.Run(execution, navmeshes.Count, index => judge.Navmesh(navmeshes[index]), ParallelMap.AutomaticRangeSize),
            judge,
            areas,
            shownTargets,
            spacesOnLand);
    }

    /// <summary>The cleaned mod's objects still standing in the columns of the changes whose areas are cleared, removed or kept by the never-rules.</summary>
    private static LeftoversRemoved RemoveLeftovers(StepInputs inputs, AreaVerdicts verdicts)
    {
        var (input, judge) = (inputs.Input, verdicts.Judge);
        var targets = input.Earlier.Collected.Targets;
        var clearedPoints = verdicts.Placed.SelectMany(judged => judge.ClearedPoints(judged))
            .Concat(verdicts.Cells.SelectMany(judged => judge.ClearedPoints(judged)))
            .Concat(verdicts.Lands.SelectMany(judged => judge.ClearedPoints(judged)))
            .Concat(verdicts.Navmeshes.SelectMany(judged => judge.ClearedPoints(judged)));
        var found = new LeftoverFinder(targets, verdicts.ShownTargets, input.Reads.Caches.Shapes, verdicts.Areas, inputs.Final).StandingIn(clearedPoints);
        return LeftoverRemoval.Remove(found, targets, inputs.Final, inputs.OtherModsObjects, input.Leftovers);
    }

    /// <summary>
    /// Every kind's plan before the guards between the kinds. A placed record the leftovers' rounds decided, or whose object the step for
    /// other mods' objects took away once they were gone, is left out, as it would have been had they been gone when it was picked.
    /// </summary>
    private static Plans PlanKinds(StepInputs inputs, AreaVerdicts verdicts, LeftoversRemoved leftovers)
    {
        var (input, final) = (inputs.Input, inputs.Final);
        var (collected, settings) = (input.Earlier.Collected, input.Settings);
        var isCandidate = new RestoreCandidates(collected, final, inputs.OtherModsObjects).CreatePlacedCandidateTest();
        PlacedChangeJudged[] placedJudged = [.. verdicts.Placed.Where(judged => isCandidate(judged.Change))];
        List<RecordChangedByCleanedMod> cleared = [.. placedJudged.Where(change => change.IsRestored).Select(change => change.Change)];
        var removedByRun = ObjectsOfTheRun.RemovedByRun(collected, final, inputs.OtherModsObjects);
        var presence = FinalPresence.Of(collected, removedByRun, cleared);
        var doors = new DoorLinks(collected.ChangedRecords, cleared);
        var ground = new GroundUnderObjects(input.Reads.Caches.Shapes, inputs.FindLandWorldspace);
        var landHeights = LandInputs.LandHeightsOf(collected, inputs.ReadLandHeights);
        var targetsThatMayStay = ObjectsOfTheRun.TargetsThatMayStay(collected, final, cleared);
        var terrain = settings.Terrain
            ? PlanTerrain(verdicts.Lands, targetsThatMayStay, presence, ground, landHeights, settings)
            : new TerrainUndoPlan([.. verdicts.LandsChanged.Select(land => new LandAreasJudged(land, Never: null, Quarters: []))], landHeights, settings.HeightChangeTolerance, settings.TerrainEdgeBlendDistance);
        var navmeshPlan = settings.Navmeshes ? PlanNavmeshes(inputs, verdicts, removedByRun, presence, doors) : null;
        var water = new WaterUndoPlan(verdicts.Cells, collected.ChangedCells, settings.WaterHeightChangeTolerance);
        return new Plans(
            placedJudged,
            cleared,
            presence,
            doors,
            verdicts.SpacesOnLand,
            ground,
            landHeights,
            targetsThatMayStay,
            leftovers,
            new RestoreState([.. cleared.Select(_ => (PlacedRefusal?)null)], navmeshPlan, terrain, water, CausesLeftOut: null));
    }

    private static (RestoreLoop Loop, TimeSpan WorldTime) BuildLoop(StepInputs inputs, Plans plans)
    {
        var (placedGuards, terrainGuards, loopInputs, worldTime) = BuildGuards(inputs, plans);
        return (new RestoreLoop(placedGuards, terrainGuards, loopInputs, inputs.Input.TimeSinceStart), worldTime);
    }

    /// <summary>
    /// The guards between the kinds over the after-removal world, with the placed guards only when a placed record's area is cleared,
    /// and the time of building that world.
    /// </summary>
    internal static (PlacedGuards? PlacedGuards, TerrainGuards? TerrainGuards, RestoreLoopInputs LoopInputs, TimeSpan WorldTime) BuildGuards(StepInputs inputs, Plans plans)
    {
        var input = inputs.Input;
        var (collected, settings, shapes) = (input.Earlier.Collected, inputs.Settings, input.Reads.Caches.Shapes);
        var reach = new GuardReach(shapes, settings.TooClose, input.Guards.HoldersOfObjectsThatStay.TouchGap);
        var (world, worldTime) = Timed.Run(
            input.TimeSinceStart, () => AfterRemovalWorld.Of(collected, inputs.Final, inputs.OtherModsObjects, input.Reads.CleanedMod, plans.Cleared, reach));
        var blocking = new ObjectsThatBlockActors(shapes, collected.WinningPrimitives);
        var landHeights = plans.LandHeights;
        var terrainGuards = settings.Terrain
            ? new TerrainGuards(() => ObjectsOfTheRun.OtherPluginsObjectsThatStay(world, collected, plans.Cleared), plans.Ground, plans.SpacesOnLand, cell => landHeights(cell).Current, settings.HeightChangeTolerance, input.Execution)
            : null;
        return (
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
                plans.Doors),
            worldTime);
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
                inputs.Settings.ChangedTerrain,
                input.Reads.Plugin.ReadTerrainWinner),
            reach,
            plans.FinalPresence,
            new DoorLinkGuard(plans.Cleared, input.Earlier.Collected.ChangedNavmeshes, guards.NavmeshCheck.FindFaultIfWritten),
            input.Execution);
    }

    /// <summary>What every candidate became once the guards between the kinds change nothing more.</summary>
    internal static RestoreWhereRemovedResult ResultOf(Plans plans, RestoreState state, int passes, RestoreWhereRemovedTimes times, WhereRemovedRestoreSettings settings)
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
            PlacedJudgments.FailedChecks(objects),
            times,
            plans.Leftovers.Judged,
            plans.Leftovers.OtherModsObjects);
    }

    /// <summary>
    /// Every navmesh the cleaned mod changed or added, judged and grouped, with the groups that fail a guard on the facts read refused.
    /// A door stands for these guards, and its link is as it ends, when every placed record whose areas let it go back goes back.
    /// </summary>
    private static NavmeshUndoPlan PlanNavmeshes(StepInputs inputs, AreaVerdicts verdicts, IReadOnlySet<RecordKey> removedByRun, FinalPresence presence, DoorLinks doors)
    {
        var (collected, reads) = (inputs.Input.Earlier.Collected, inputs.Input.Reads);
        var byKey = verdicts.ChangedNavmeshes.ToDictionary(navmesh => navmesh.Navmesh);
        var records = collected.ChangedRecords.ToDictionary(record => record.Key);
        var doorsIfAllGoBack = new DoorsAsTheyEnd(presence, doors, _ => true);
        return new NavmeshUndoPlan(
            verdicts.Navmeshes,
            sunk => new NavmeshGroupGuards(byKey, sunk, records, removedByRun.Contains, doors, doorsIfAllGoBack, reads.Plugins, reads.CleanedMod),
            inputs.Input.Guards.NavmeshCheck.FindFaultIfWritten);
    }

    /// <summary>
    /// The LAND records with their quarters judged, planned with the ground under every target that stands where it stands last
    /// protected where it reaches the ground. A target stands when it ends present with every placed record whose areas let it
    /// go back going back; the loop protects the ground under the others once a refusal lets them stand.
    /// </summary>
    /// <param name="judged">In the order of their cells.</param>
    private static TerrainUndoPlan PlanTerrain(
        LandAreasJudged[] judged,
        IReadOnlyList<TargetObject> targetsThatMayStay,
        FinalPresence presence,
        GroundUnderObjects ground,
        Func<ExteriorCell, LandHeights> landHeights,
        WhereRemovedRestoreSettings settings)
    {
        var plan = new TerrainUndoPlan(judged, landHeights, settings.HeightChangeTolerance, settings.TerrainEdgeBlendDistance);
        if (judged.Length == 0) return plan;

        var standing = targetsThatMayStay.Where(target => !presence.EndsRemoved(target.Key, _ => true));
        return plan.Protect(TerrainKeptReason.UnderObjectThatStays, ground.UnderWhereTheyReachTheGround(standing, plan.CurrentHeightsOf));
    }

    /// <summary>What <see cref="Plan"/> was given, with the step switched on, and the decisions and other mods' objects the step reads.</summary>
    /// <param name="Final">The decisions with every round and every move; once the leftovers are removed, with their rounds too.</param>
    /// <param name="OtherModsObjects">What became of other mods' objects left without support under <paramref name="Final"/>.</param>
    internal sealed record StepInputs(Input Input, IRemovalDecisions Final, OtherModsObjectsResult OtherModsObjects)
    {
        public WhereRemovedRestoreSettings Settings => Input.Settings;

        /// <returns>The worldspace whose land the space uses; null for a space without land.</returns>
        public RecordKey? FindLandWorldspace(RecordKey space) => Input.Reads.Plugin.FindLandWorldspace(space);

        /// <summary>The heights of the cell's LAND record at its version just below the cleaned mod's and at the winning one.</summary>
        public LandHeights ReadLandHeights(ExteriorCell cell) => new(Input.Reads.Plugin.ReadTerrainBelow(Input.Reads.CleanedMod, cell), Input.Reads.Plugin.ReadTerrain(cell));
    }

    /// <summary>Every candidate judged by the never-rules and its areas, with what judged them.</summary>
    /// <param name="Placed">Every placed record, in key order.</param>
    /// <param name="Cells">Every cell record, in key order.</param>
    /// <param name="LandsChanged">Every LAND record with an undone part, in the order of their cells.</param>
    /// <param name="Lands">Those records judged, when the terrain is restored; none otherwise.</param>
    /// <param name="ChangedNavmeshes">Every navmesh the cleaned mod changed or added, in key order, when navmeshes are restored; none otherwise.</param>
    /// <param name="Navmeshes">Those navmeshes judged, in the same order.</param>
    /// <param name="ShownTargets">The shown targets where the rounds measure them, which the areas count.</param>
    private sealed record AreaVerdicts(
        PlacedChangeJudged[] Placed,
        ImmutableArray<CellChangeJudged> Cells,
        ImmutableArray<LandChangedByCleanedMod> LandsChanged,
        LandAreasJudged[] Lands,
        List<NavmeshChangedByCleanedMod> ChangedNavmeshes,
        NavmeshChangeJudged[] Navmeshes,
        ChangeJudge Judge,
        ClearedAreaJudge Areas,
        IVisibleTargetObjects ShownTargets,
        Dictionary<RecordKey, ImmutableArray<RecordKey>> SpacesOnLand);

    /// <summary>The candidates judged and every kind's plan before the guards between the kinds.</summary>
    /// <param name="PlacedJudged">Every placed record judged by the never-rules and its areas, in key order.</param>
    /// <param name="Cleared">The placed records whose areas let them go back, in key order, each with the parts its restore writes.</param>
    /// <param name="Leftovers">The leftovers in the cleared areas, and the decisions and other mods' objects with those removed gone.</param>
    /// <param name="Start">The state before the first pass.</param>
    internal sealed record Plans(
        PlacedChangeJudged[] PlacedJudged,
        List<RecordChangedByCleanedMod> Cleared,
        FinalPresence FinalPresence,
        DoorLinks Doors,
        Dictionary<RecordKey, ImmutableArray<RecordKey>> SpacesOnLand,
        GroundUnderObjects Ground,
        Func<ExteriorCell, LandHeights> LandHeights,
        List<TargetObject> TargetsThatMayStay,
        LeftoversRemoved Leftovers,
        RestoreState Start);
}
