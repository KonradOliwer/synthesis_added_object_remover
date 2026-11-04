using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Decides which changes the cleaned mod made to other mods' objects, cells, terrain and navmeshes go back where it was removed from a wide area, keeping
/// every part a later plugin changed and everything that stays. It does not write.
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

    /// <summary>Where the step reads the load order.</summary>
    /// <param name="Caches">Index the shown targets where the rounds measure them; their shapes and navmeshes are read.</param>
    /// <param name="Plugin">Where the land a space uses and the heights and winning plugin of a cell's LAND record are read.</param>
    public sealed record LoadOrderReads(IRunCaches Caches, IPluginRecords Plugin, LoadOrderPlugins Plugins, PluginName CleanedMod);

    /// <summary>What the removal steps judge each target with: the settings of its collision group, and the world they accept.</summary>
    /// <param name="ChangedTerrain">The changed-terrain step's settings of each group, with the targets it judges with them.</param>
    /// <param name="TooClose">The too-close step's settings of each group, with the targets it judges with them.</param>
    /// <param name="Accepted">The world the removal steps judge other mods' objects in, which tells which objects can cause removals.</param>
    /// <param name="LeftBehindNeighbours">The neighbours each left-behind step measures on <see cref="Input.Decisions"/>; a step that is switched off measures none.</param>
    /// <param name="CleanedModsObjects">What the removal steps judge the cleaned mod's own objects with, which a planned copy is judged with.</param>
    public sealed record RemovalStepJudging(
        ImmutableArray<ObjectsJudgedWith<ChangedTerrainSettings>> ChangedTerrain,
        ImmutableArray<ObjectsJudgedWith<TooCloseSettings>> TooClose,
        AcceptedWorld Accepted,
        ImmutableArray<ILeftBehindNeighbours> LeftBehindNeighbours,
        CleanedModsObjectRules CleanedModsObjects);

    /// <param name="Decisions">The decisions with every round and every move.</param>
    /// <param name="ClearedAreas">Judges the area around a change on <paramref name="Decisions"/> with the step's cleared-area settings.</param>
    /// <param name="RemovalSteps">What the removal steps judge each target with, which the guards judge what stays with.</param>
    /// <param name="Settings">The step is switched off when none of its kinds is on.</param>
    /// <param name="TimeSinceStart">The run's clock, which times the parts of the step.</param>
    public sealed record Input(
        EarlierResults Earlier,
        IRemovalDecisions Decisions,
        IClearedAreas ClearedAreas,
        GuardRules Guards,
        RemovalStepJudging RemovalSteps,
        LoadOrderReads Reads,
        Execution Execution,
        WhereRemovedRestoreSettings Settings,
        Func<TimeSpan> TimeSinceStart);

    /// <summary>The part the measurement failures of the objects that stay, in the worlds the guards judge in, are recorded under.</summary>
    public static PartKey MeasurementFailurePart() => RestoreWhereRemovedParts.ObjectThatStays;

    /// <returns>
    /// Each candidate with what its area, the never-rules and the guards say, in record key order (LAND records in the order of their
    /// cells); a kind whose switch is off has none. <see cref="StepOutcome.SwitchedOff"/> when no kind is on.
    /// An unexpected error stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and nothing judged.
    /// </returns>
    public static RestoreWhereRemovedResult Plan(Input input)
    {
        if (!input.Settings.Runs) return RestoreWhereRemovedResult.SwitchedOff();

        return Failures.Attempt(
            () => PlanEverything(input),
            failure => new RestoreWhereRemovedResult(
                StepOutcome.StoppedByUnexpectedError,
                Objects: [],
                Cells: [],
                Lands: [],
                Navmeshes: [],
                NavmeshGroups: [],
                Passes: 0,
                DoorsLeftRemoved: [],
                Failures: [PartFailure.OfWholePart(RestoreWhereRemovedParts.Step, failure)],
                Times: RestoreWhereRemovedTimes.None));
    }

    private static RestoreWhereRemovedResult PlanEverything(Input input)
    {
        var clock = input.TimeSinceStart;
        var (planned, plansTime) = Timed.Run(clock, () => BuildPlans(input));
        var (guards, guardsTime) = Timed.Run(clock, () => RestoreGuardsBuilder.Build(planned.Inputs, planned.Plans));
        var loop = new RestoreLoop(guards.Placed, guards.Terrain, guards.LoopInputs, clock);
        var ((state, passes, placedGuardsTime), passesTime) = Timed.Run(clock, () => loop.Run(planned.Plans.Start));
        var times = new RestoreWhereRemovedTimes(plansTime, guards.WorldTime, guardsTime - guards.WorldTime, passesTime, placedGuardsTime);
        return ResultOf(planned.Plans, state, passes, times, input.Settings);
    }

    /// <summary>The candidates judged by the never-rules and their areas on the removal steps' decisions, and every kind's plan before the guards between the kinds.</summary>
    private static PlannedRestore BuildPlans(Input input)
    {
        var inputs = new StepInputs(input);
        return new PlannedRestore(inputs, PlanKinds(inputs, JudgePlannedClones(inputs, JudgeAreas(inputs))));
    }

    /// <summary>The verdicts with every planned copy judged by the changed-terrain rule of the cleaned mod's own objects.</summary>
    private static AreaVerdicts JudgePlannedClones(StepInputs inputs, AreaVerdicts verdicts)
    {
        var input = inputs.Input;
        return verdicts with
        {
            Placed = PlannedCloneChecks.Judge(verdicts.Placed, input.Earlier.Collected.Targets, input.RemovalSteps.CleanedModsObjects.ChangedTerrain, input.Execution),
        };
    }

    /// <summary>Every candidate judged by the never-rules and its areas; a kind whose switch is off has none.</summary>
    private static AreaVerdicts JudgeAreas(StepInputs inputs)
    {
        var (input, settings, final) = (inputs.Input, inputs.Settings, inputs.Final);
        var (collected, execution) = (input.Earlier.Collected, input.Execution);
        var spacesOnLand = LandInputs.SpacesOnLand(collected, inputs.FindLandWorldspace);
        var candidates = new RestoreCandidates(collected, final, inputs.OtherModsObjects);
        var placed = settings.OtherModsObjects ? candidates.PlacedRecords() : [];
        var clones = CloneCandidates.Of(collected, final, input.RemovalSteps.LeftBehindNeighbours, input.Reads.Caches, execution, placed);
        var judge = new ChangeJudge(collected.Targets, input.ClearedAreas, input.Reads.Plugins, spacesOnLand, clones);
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
            spacesOnLand);
    }

    /// <summary>Every kind's plan before the guards between the kinds.</summary>
    private static Plans PlanKinds(StepInputs inputs, AreaVerdicts verdicts)
    {
        var (input, final) = (inputs.Input, inputs.Final);
        var (collected, settings) = (input.Earlier.Collected, input.Settings);
        var goingBack = verdicts.Placed.Where(change => change.IsRestored).ToList();
        List<RecordChangedByCleanedMod> cleared = [.. goingBack.Select(change => change.Change)];
        var removedByRun = ObjectsOfTheRun.RemovedByRun(collected, final, inputs.OtherModsObjects);
        var presence = FinalPresence.Of(collected, removedByRun, cleared);
        var doors = new DoorLinks(collected.ChangedRecords, cleared);
        var ground = new GroundUnderObjects(input.Reads.Caches.Shapes, inputs.FindLandWorldspace);
        var landHeights = LandInputs.LandHeightsOf(collected, inputs.ReadLandHeights);
        var targetsThatMayStay = ObjectsOfTheRun.TargetsThatMayStay(collected, final, cleared);
        var clones = goingBack.Where(change => change.Cloning is not null).Select(change => CloneCandidates.AsClone(collected.Targets[change.Change.Target!.Value.Index]));
        var terrain = settings.Terrain
            ? PlanTerrain(verdicts.Lands, targetsThatMayStay, clones, presence, ground, landHeights, settings)
            : new TerrainUndoPlan([.. verdicts.LandsChanged.Select(land => new LandAreasJudged(land, Never: null, Quarters: []))], landHeights, settings.HeightChangeTolerance, settings.TerrainEdgeBlendDistance);
        var navmeshPlan = settings.Navmeshes ? PlanNavmeshes(inputs, verdicts, removedByRun, presence, doors) : null;
        var water = new WaterUndoPlan(verdicts.Cells, collected.ChangedCells, settings.WaterHeightChangeTolerance);
        return new Plans(
            verdicts.Placed,
            cleared,
            presence,
            doors,
            verdicts.SpacesOnLand,
            ground,
            landHeights,
            targetsThatMayStay,
            new RestoreState([.. cleared.Select(_ => (PlacedRefusal?)null)], [.. goingBack.Select(change => change.Cloning)], navmeshPlan, terrain, water, CausesLeftOut: null));
    }

    /// <summary>What every candidate became once the guards between the kinds change nothing more.</summary>
    private static RestoreWhereRemovedResult ResultOf(Plans plans, RestoreState state, int passes, RestoreWhereRemovedTimes times, WhereRemovedRestoreSettings settings)
    {
        var objects = PlacedJudgments.WithOutcomes(plans.PlacedJudged, plans.Cleared, state.Placed, state.Clones);
        return new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            objects,
            state.Water.Judged(),
            settings.Terrain ? state.Terrain.Judged() : [],
            state.Navmeshes?.Navmeshes() ?? [],
            state.Navmeshes?.Groups() ?? [],
            passes,
            plans.FinalPresence.DoorsEndingRemoved(state.PlacedGoingBack().Contains),
            PlacedJudgments.FailedChecks(objects),
            times);
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
        var doorsIfAllGoBack = new DoorsAsTheyEnd(new PresenceInPass(presence, _ => true), doors);
        return new NavmeshUndoPlan(
            verdicts.Navmeshes,
            sunk => new NavmeshGroupGuards(byKey, sunk, records, removedByRun.Contains, doors, doorsIfAllGoBack, reads.Plugins, reads.CleanedMod),
            inputs.Input.Guards.NavmeshCheck.FindFaultIfWritten);
    }

    /// <summary>
    /// The LAND records with their quarters judged, planned with the ground under every target that stands where it stands last and
    /// every planned clone protected where it reaches the ground. A target stands when it ends present with every placed record
    /// whose areas let it go back going back; the loop protects the ground under the others once a refusal lets them stand.
    /// </summary>
    /// <param name="judged">In the order of their cells.</param>
    /// <param name="clones">The clones planned for the placed records whose areas let them go back, where those records stand now.</param>
    private static TerrainUndoPlan PlanTerrain(
        LandAreasJudged[] judged,
        IReadOnlyList<TargetObject> targetsThatMayStay,
        IEnumerable<TargetObject> clones,
        FinalPresence presence,
        GroundUnderObjects ground,
        Func<ExteriorCell, LandHeights> landHeights,
        WhereRemovedRestoreSettings settings)
    {
        var plan = new TerrainUndoPlan(judged, landHeights, settings.HeightChangeTolerance, settings.TerrainEdgeBlendDistance);
        if (judged.Length == 0) return plan;

        var standing = targetsThatMayStay.Where(target => !presence.EndsRemoved(target.Key, _ => true)).Concat(clones);
        return plan.Protect(TerrainKeptReason.UnderObjectThatStays, ground.UnderWhereTheyReachTheGround(standing, plan.CurrentHeightsOf));
    }

    /// <summary>What <see cref="Plan"/> was given, with the step switched on.</summary>
    internal sealed record StepInputs(Input Input)
    {
        public WhereRemovedRestoreSettings Settings => Input.Settings;

        /// <summary>The decisions with every round and every move.</summary>
        public IRemovalDecisions Final => Input.Decisions;

        /// <summary>What became of other mods' objects left without support under <see cref="Final"/>.</summary>
        public OtherModsObjectsResult OtherModsObjects => Input.Earlier.OtherModsObjects;

        /// <summary>How far the placed guards that read the object caches look.</summary>
        public GuardReach Reach { get; } = new(Input.Reads.Caches.Shapes, Input.Settings.TooClose, Input.Guards.HoldersOfObjectsThatStay.TouchGap);

        /// <returns>The worldspace whose land the space uses; null for a space without land.</returns>
        public RecordKey? FindLandWorldspace(RecordKey space) => Input.Reads.Plugin.FindLandWorldspace(space);

        /// <summary>The heights of the cell's LAND record at its version just below the cleaned mod's and at the winning one.</summary>
        public LandHeights ReadLandHeights(ExteriorCell cell) => new(Input.Reads.Plugin.ReadTerrainBelow(Input.Reads.CleanedMod, cell), Input.Reads.Plugin.ReadTerrain(cell));
    }

    /// <param name="Inputs">What the plans and the guards read.</param>
    internal sealed record PlannedRestore(StepInputs Inputs, Plans Plans);

    /// <summary>Every candidate judged by the never-rules and its areas, with what judged them.</summary>
    /// <param name="Placed">Every placed record, in key order.</param>
    /// <param name="Cells">Every cell record, in key order.</param>
    /// <param name="LandsChanged">Every LAND record with an undone part, in the order of their cells.</param>
    /// <param name="Lands">Those records judged, when the terrain is restored; none otherwise.</param>
    /// <param name="ChangedNavmeshes">Every navmesh the cleaned mod changed or added, in key order, when navmeshes are restored; none otherwise.</param>
    /// <param name="Navmeshes">Those navmeshes judged, in the same order.</param>
    private sealed record AreaVerdicts(
        PlacedChangeJudged[] Placed,
        ImmutableArray<CellChangeJudged> Cells,
        ImmutableArray<LandChangedByCleanedMod> LandsChanged,
        LandAreasJudged[] Lands,
        List<NavmeshChangedByCleanedMod> ChangedNavmeshes,
        NavmeshChangeJudged[] Navmeshes,
        Dictionary<RecordKey, ImmutableArray<RecordKey>> SpacesOnLand);

    /// <summary>The candidates judged and every kind's plan before the guards between the kinds.</summary>
    /// <param name="PlacedJudged">Every placed record judged by the never-rules and its areas, in key order.</param>
    /// <param name="Cleared">The placed records whose areas let them go back, in key order, each with the parts its restore writes.</param>
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
        RestoreState Start);
}
