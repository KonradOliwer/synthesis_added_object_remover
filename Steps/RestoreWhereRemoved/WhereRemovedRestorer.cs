using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
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
    /// <param name="TooClose">The too-close rule over the world of what stays that the step builds.</param>
    /// <param name="FollowUp">The removal steps' follow-up of what rests on or touches an object leaving its place, over the run's world.</param>
    /// <param name="TouchGap">The largest gap between two surfaces that the support test counts as touching.</param>
    /// <param name="NavmeshCheck">The navmesh check of a trial write.</param>
    public sealed record GuardRules(
        ITooCloseToWhatStaysRule TooClose, IFollowUpOfObjectsLeaving FollowUp, float TouchGap, INavmeshCheckIfWritten NavmeshCheck);

    /// <summary>Where the step reads the load order.</summary>
    /// <param name="Caches">Index the shown targets where the rounds measure them; their shapes and navmeshes are read.</param>
    /// <param name="Plugin">Where the land a space uses and the heights and winning plugin of a cell's LAND record are read.</param>
    public sealed record LoadOrderReads(IRunCaches Caches, IPluginRecords Plugin, LoadOrderPlugins Plugins, PluginName CleanedMod);

    /// <summary>What the removal steps judge each target with: the settings of its collision group, and the world they accept.</summary>
    /// <param name="ChangedTerrain">The changed-terrain rule's settings of each target it judges.</param>
    /// <param name="TooClose">The too-close rule's settings of each target it judges.</param>
    /// <param name="Accepted">The world the removal steps judge other mods' objects in, which tells which objects can cause removals.</param>
    /// <param name="LeftBehindNeighbours">The neighbours each left-behind step measures on <see cref="Input.Decisions"/>; a step that is switched off measures none.</param>
    /// <param name="CleanedModsObjects">What the removal steps judge the cleaned mod's own objects with, which a planned copy is judged with.</param>
    /// <param name="ChangedTerrainRule">The changed-terrain rule in <paramref name="Accepted"/>, over the load order as read and over the terrain a pass plans.</param>
    public sealed record RemovalStepJudging(
        ImmutableDictionary<TargetId, ChangedTerrainSettings> ChangedTerrain,
        ImmutableDictionary<TargetId, TooCloseSettings> TooClose,
        AcceptedWorld Accepted,
        ImmutableArray<ILeftBehindNeighbours> LeftBehindNeighbours,
        CleanedModsObjectRules CleanedModsObjects,
        IChangedTerrainJudging ChangedTerrainRule);

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
        var ((state, followUp, passes, inPasses, terrainFindingsLeft), passesTime) = Timed.Run(clock, () => loop.Run(planned.Plans.Start));
        var times = new RestoreWhereRemovedTimes(plansTime, guards.WorldTime, guardsTime - guards.WorldTime, passesTime, inPasses);
        return ResultOf(planned.Plans, state, new LoopEnd(followUp, passes, terrainFindingsLeft), times, input.Settings);
    }

    /// <summary>How the loop ended: with what follow-up, after how many passes, and how many changed-terrain findings were left with nothing to keep.</summary>
    private sealed record LoopEnd(RestoreFollowUp FollowUp, int Passes, int TerrainFindingsLeft);

    /// <summary>The candidates judged by the never-rules and their areas on the removal steps' decisions, and every kind's plan before the guards between the kinds.</summary>
    internal static PlannedRestore BuildPlans(Input input)
    {
        var inputs = new StepInputs(input);
        return new PlannedRestore(inputs, PlanKinds(inputs, JudgePlannedClones(inputs, JudgeAreas(inputs))));
    }

    /// <summary>The verdicts with every planned copy judged by the changed-terrain rule of the cleaned mod's own objects where its original stands now.</summary>
    private static AreaVerdicts JudgePlannedClones(StepInputs inputs, AreaVerdicts verdicts) =>
        verdicts with { Placed = inputs.TerrainChecks.JudgeCopiesOnTheBaseline(verdicts.Placed, inputs.Input.Earlier.Collected.Targets) };

    /// <summary>Every candidate judged by the never-rules and its areas; a kind whose switch is off has none.</summary>
    private static AreaVerdicts JudgeAreas(StepInputs inputs)
    {
        var (input, settings, final) = (inputs.Input, inputs.Settings, inputs.Final);
        var (collected, execution) = (input.Earlier.Collected, input.Execution);
        var spacesOnLand = LandInputs.SpacesOnLand(collected, inputs.FindLandWorldspace);
        var candidates = new RestoreCandidates(collected, final, inputs.OtherModsObjects);
        var placed = settings.OtherModsObjects ? candidates.PlacedRecords() : [];
        var clones = CloneCandidates.Of(collected, final, input.RemovalSteps.LeftBehindNeighbours, input.Reads.Caches, execution, placed);
        var judge = new ChangeJudge(collected.Targets, input.ClearedAreas, input.Reads.Plugins, spacesOnLand, clones, SharedGrids.Of(collected));
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
        var goingBack = verdicts.Placed.Where(change => change.IsRestored).ToList();
        var objects = ObjectsAsTheyEnd(inputs, [.. goingBack.Select(change => change.Change)]);
        var ground = new GroundUnderObjects(inputs.Input.Reads.Caches.Shapes, inputs.FindLandWorldspace);
        var landHeights = LandInputs.LandHeightsOf(inputs.Input.Earlier.Collected, inputs.ReadLandHeights);
        var start = new RestoreState(
            [.. goingBack.Select(_ => (PlacedRefusal?)null)],
            [.. goingBack.Select(change => change.Cloning)],
            inputs.Settings.Navmeshes ? PlanNavmeshes(inputs, verdicts, objects) : null,
            PlanTerrain(inputs, verdicts, objects, ClonesOf(inputs, goingBack), ground, landHeights),
            PlanWater(inputs, verdicts.Cells),
            ImmutableDictionary<TerrainChecked, PartFailure>.Empty);
        return new Plans(
            verdicts.Placed, objects.Cleared, objects.FinalPresence, objects.Doors, verdicts.SpacesOnLand, ground, landHeights, objects.TargetsThatMayStay, start);
    }

    /// <summary>The placed records whose areas let them go back, and the run's objects as they end when every one of them goes back.</summary>
    /// <param name="cleared">In key order.</param>
    private static PlacedObjectsAsTheyEnd ObjectsAsTheyEnd(StepInputs inputs, List<RecordChangedByCleanedMod> cleared)
    {
        var collected = inputs.Input.Earlier.Collected;
        var removedByRun = ObjectsOfTheRun.RemovedByRun(collected, inputs.Final, inputs.OtherModsObjects);
        return new PlacedObjectsAsTheyEnd(
            cleared,
            removedByRun,
            FinalPresence.Of(collected, removedByRun, cleared),
            new DoorLinks(collected.ChangedRecords, cleared),
            ObjectsOfTheRun.TargetsThatMayStay(collected, inputs.Final, cleared));
    }

    /// <summary>The copies planned for the placed records going back with one, where those records stand now.</summary>
    private static IEnumerable<TargetObject> ClonesOf(StepInputs inputs, List<PlacedChangeJudged> goingBack) =>
        goingBack.Where(change => change.Cloning is { RefusedBy: null }).Select(change => CloneCandidates.AsClone(inputs.Input.Earlier.Collected.Targets[change.Change.Target!.Value.Index]));

    private static WaterUndoPlan PlanWater(StepInputs inputs, ImmutableArray<CellChangeJudged> cells) =>
        new(cells, inputs.Input.Earlier.Collected.ChangedCells, inputs.Settings.WaterHeightChangeTolerance);

    /// <param name="Cleared">The placed records whose areas let them go back, in key order.</param>
    /// <param name="RemovedByRun">The placed records the rounds or the other mods' objects step remove.</param>
    /// <param name="TargetsThatMayStay">The targets that are not removed and are no candidates, where they stand last.</param>
    private sealed record PlacedObjectsAsTheyEnd(
        List<RecordChangedByCleanedMod> Cleared, IReadOnlySet<RecordKey> RemovedByRun, FinalPresence FinalPresence, DoorLinks Doors, List<TargetObject> TargetsThatMayStay);

    /// <summary>What every candidate became once the guards between the kinds change nothing more.</summary>
    private static RestoreWhereRemovedResult ResultOf(Plans plans, RestoreState state, LoopEnd end, RestoreWhereRemovedTimes times, WhereRemovedRestoreSettings settings)
    {
        var objects = PlacedJudgments.WithOutcomes(plans.PlacedJudged, plans.Cleared, state.Placed, state.Clones);
        return new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            objects,
            state.Water.Judged(),
            settings.Terrain ? state.Terrain.Judged() : [],
            state.Navmeshes?.Navmeshes() ?? [],
            state.Navmeshes?.Groups() ?? [],
            end.Passes,
            plans.FinalPresence.DoorsEndingRemoved(state.PlacedGoingBack().Contains),
            [.. PlacedJudgments.FailedChecks(objects), .. state.TerrainCheckFailures()],
            times,
            end.TerrainFindingsLeft) { FollowUp = end.FollowUp };
    }

    /// <summary>
    /// Every navmesh the cleaned mod changed or added, judged and grouped, with the groups that fail a guard on the facts read refused.
    /// A door stands for these guards, and its link is as it ends, when every placed record whose areas let it go back goes back.
    /// </summary>
    private static NavmeshUndoPlan PlanNavmeshes(StepInputs inputs, AreaVerdicts verdicts, PlacedObjectsAsTheyEnd objects)
    {
        var (collected, reads) = (inputs.Input.Earlier.Collected, inputs.Input.Reads);
        var byKey = verdicts.ChangedNavmeshes.ToDictionary(navmesh => navmesh.Navmesh);
        var records = collected.ChangedRecords.ToDictionary(record => record.Key);
        var doorsIfAllGoBack = new DoorsAsTheyEnd(new PresenceInPass(objects.FinalPresence, _ => true), objects.Doors);
        return new NavmeshUndoPlan(
            verdicts.Navmeshes,
            sunk => new NavmeshGroupGuards(byKey, sunk, records, objects.RemovedByRun.Contains, objects.Doors, doorsIfAllGoBack, reads.Plugins, reads.CleanedMod),
            inputs.Input.Guards.NavmeshCheck.FindFaultIfWritten);
    }

    /// <summary>
    /// The LAND records with their quarters judged, with the ground under what stands protected; while the terrain is switched off,
    /// every LAND record with an undone part, with nothing going back.
    /// </summary>
    /// <param name="clones">The clones planned for the placed records whose areas let them go back, where those records stand now.</param>
    private static TerrainUndoPlan PlanTerrain(
        StepInputs inputs, AreaVerdicts verdicts, PlacedObjectsAsTheyEnd objects, IEnumerable<TargetObject> clones, GroundUnderObjects ground, Func<ExteriorCell, LandHeights> landHeights) =>
        inputs.Settings.Terrain
            ? PlanTerrainGoingBack(inputs, verdicts.Lands, objects, clones, ground, landHeights)
            : TerrainPlanOf(inputs, [.. verdicts.LandsChanged.Select(land => new LandAreasJudged(land, Never: null, Quarters: []))], landHeights);

    /// <param name="judged">In the order of their cells.</param>
    private static TerrainUndoPlan TerrainPlanOf(StepInputs inputs, LandAreasJudged[] judged, Func<ExteriorCell, LandHeights> landHeights) =>
        new(judged, landHeights, inputs.Settings.HeightChangeTolerance, inputs.Settings.TerrainEdgeBlendDistance);

    /// <summary>
    /// The plan with the ground under every target that stands where it stands last, every one of the cleaned mod's objects a later
    /// plugin holds and every planned clone protected where it reaches the ground. A target stands when it ends present with every
    /// placed record whose areas let it go back going back; the loop protects the ground under the others once a refusal lets them stand.
    /// </summary>
    /// <param name="judged">In the order of their cells.</param>
    private static TerrainUndoPlan PlanTerrainGoingBack(
        StepInputs inputs, LandAreasJudged[] judged, PlacedObjectsAsTheyEnd objects, IEnumerable<TargetObject> clones, GroundUnderObjects ground, Func<ExteriorCell, LandHeights> landHeights)
    {
        var plan = TerrainPlanOf(inputs, judged, landHeights);
        if (judged.Length == 0) return plan;

        var standing = objects.TargetsThatMayStay.Where(target => !objects.FinalPresence.EndsRemoved(target.Key, _ => true))
            .Concat(inputs.Input.Earlier.Collected.CleanedModsObjectsOverriddenLater)
            .Concat(clones);
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
        public GuardReach Reach { get; } = new(Input.Reads.Caches.Shapes, Input.Guards.TooClose, Input.Settings.TooClose, Input.Guards.TouchGap);

        /// <summary>The changed-terrain rule over the objects that stay and the copies, with its baseline checks made once for the step.</summary>
        public StandingTerrainChecks TerrainChecks { get; } = new(
            Input.RemovalSteps.ChangedTerrainRule,
            Input.RemovalSteps.Accepted.Patch,
            Input.Reads.CleanedMod,
            TerrainSettingsByKey(Input.Earlier.Collected, Input.RemovalSteps),
            Input.RemovalSteps.CleanedModsObjects.ChangedTerrainSettings,
            Input.Execution);

        /// <returns>The worldspace whose land the space uses; null for a space without land.</returns>
        public RecordKey? FindLandWorldspace(RecordKey space) => Input.Reads.Plugin.FindLandWorldspace(space);

        /// <summary>The heights of the cell's LAND record at its version just below the cleaned mod's and at the winning one.</summary>
        public LandHeights ReadLandHeights(ExteriorCell cell) => new(Input.Reads.Plugin.ReadTerrainBelow(Input.Reads.CleanedMod, cell), Input.Reads.Plugin.ReadTerrain(cell));

        /// <summary>The changed-terrain settings each target is judged with, by its key, since a copy and a candidate where it stands now carry no target id.</summary>
        private static Dictionary<RecordKey, ChangedTerrainSettings> TerrainSettingsByKey(CollectedObjects collected, RemovalStepJudging judging) =>
            judging.ChangedTerrain.ToDictionary(judged => collected.Targets[judged.Key.Index].Key, judged => judged.Value);
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
