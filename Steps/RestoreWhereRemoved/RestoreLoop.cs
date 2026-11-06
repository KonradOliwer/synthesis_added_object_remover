using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the guards between the kinds have refused, protected and kept so far. Each part only ever grows.</summary>
/// <param name="Placed">The first refusal of each placed record whose areas let it go back, by its index among them; null for one that goes back.</param>
/// <param name="Clones">
/// The copy rule's outcome of each of those records, by the same index: null for one without a planned copy; one whose copy is lost
/// carries the rule that lost it and no longer goes back. A record never has both a refusal and a lost copy.
/// </param>
/// <param name="Navmeshes">Null while the navmeshes are switched off.</param>
/// <param name="Terrain">While the terrain is switched off, a plan in which nothing goes back.</param>
/// <param name="TerrainCheckStops">The first failure of each object whose changed-terrain check over a plan stopped.</param>
internal sealed record RestoreState(
    ImmutableArray<PlacedRefusal?> Placed,
    ImmutableArray<CloneJudged?> Clones,
    NavmeshUndoPlan? Navmeshes,
    TerrainUndoPlan Terrain,
    WaterUndoPlan Water,
    ImmutableDictionary<TerrainChecked, PartFailure> TerrainCheckStops)
{
    /// <summary>The indexes of the placed records that go back, with or without a copy.</summary>
    public IReadOnlySet<int> PlacedGoingBack() => Enumerable.Range(0, Placed.Length).Where(index => Placed[index] is null && Clones[index] is not { RefusedBy: not null }).ToHashSet();

    /// <summary>The indexes of the placed records that go back with a copy.</summary>
    public IReadOnlySet<int> GoingBackWithClone() => Enumerable.Range(0, Placed.Length).Where(index => Placed[index] is null && Clones[index] is { RefusedBy: null }).ToHashSet();

    /// <summary>How many placed records no longer go back: those refused and those whose copy is lost.</summary>
    public int PlacedStoppedCount => Placed.Count(refusal => refusal is not null) + Clones.Count(clone => clone is { RefusedBy: not null });

    /// <summary>
    /// How many refusals, lost copies, protected points, kept textures and water levels and stopped terrain checks it holds; since none
    /// is taken back, an equal count means nothing changed.
    /// </summary>
    public int Count => PlacedStoppedCount + (Navmeshes?.RefusedCount ?? 0) + Terrain.ProtectedCount + Terrain.TexturesKeptCount + Water.KeptCount + TerrainCheckStops.Count;

    /// <summary>The failures of the stopped changed-terrain checks, in key order of their objects, the originals before their copies.</summary>
    public IEnumerable<PartFailure> TerrainCheckFailures() =>
        TerrainCheckStops.OrderBy(stop => stop.Key.Key, RecordKeyOrder.Comparer).ThenBy(stop => stop.Key.IsCopy).Select(stop => stop.Value);
}

/// <summary>What every pass of the loop reads that does not change between passes.</summary>
/// <param name="World">The after-removal world over the placed records whose areas let them go back.</param>
/// <param name="TargetsThatMayStay">The targets that are not removed and not among the placed records whose areas let them go back, where they stand last.</param>
/// <param name="FindLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
/// <param name="TerrainChecks">The changed-terrain rule over the terrain and water each pass plans.</param>
/// <param name="GroundBeforeAndAfter">A cell's heights before the cleaned mod and as the given terrain plan ends them.</param>
/// <param name="Winning">The winning navmeshes.</param>
/// <param name="ChangedNavmeshes">Every navmesh whose triangles the cleaned mod changed or added, in key order.</param>
/// <param name="ChangedNavmeshTriangles">The triangles of every navmesh the cleaned mod changed or added as they stand, in key order of their navmeshes.</param>
/// <param name="FinalPresence">Whether a placed record ends removed in the final world, given which placed records go back.</param>
/// <param name="Doors">Where each door's navmesh link points, given which placed records go back.</param>
/// <param name="FollowUp">The removal steps' follow-up of the placed records leaving where they stand now and of the ground going down.</param>
/// <param name="LandOf">The ground and water a terrain plan and a water plan leave.</param>
/// <param name="StandingTooClose">The too-close check of the cleaned mod's objects that stand, as the next patcher makes it, made on first use.</param>
/// <param name="BuriedByGroundGoingUp">The objects of plugins trusted in full placed for the ground as it is that ground going back up would bury.</param>
internal sealed record RestoreLoopInputs(
    AfterRemovalWorld World,
    StandingObjects Standing,
    IReadOnlyList<TargetObject> TargetsThatMayStay,
    GroundUnderObjects Ground,
    StandingTerrainChecks TerrainChecks,
    Func<RecordKey, RecordKey?> FindLandWorldspace,
    Func<TerrainUndoPlan, ExteriorCell, LandHeights> GroundBeforeAndAfter,
    float HeightTolerance,
    INavmeshes Winning,
    ImmutableArray<RecordKey> ChangedNavmeshes,
    ImmutableArray<NavmeshTriangleInCell> ChangedNavmeshTriangles,
    FinalPresence FinalPresence,
    DoorLinks Doors,
    FollowUpOfGoingBack FollowUp,
    Func<TerrainUndoPlan, WaterUndoPlan, LandAfter> LandOf,
    ComputedOnce<StandingTooCloseChecks> StandingTooClose,
    ObjectsBuriedByGroundGoingUp BuriedByGroundGoingUp);

/// <summary>
/// Repeats the guards between the kinds until nothing more changes: the placed records failing their guards against the current
/// plans are refused, and those whose copy fails its rules (<see cref="CloneGuards"/>) stay, the navmesh groups failing the ground,
/// object and water guards or the door guard with the doors as they end; the grid points under the objects of plugins trusted in full
/// whose support the ground going down would take and that the follow-up may not take along (<see cref="FollowUpChecks.GroundHolds"/>)
/// or would pull down too close to what stays (<see cref="StandingTooCloseFound.GroundKeptUnder"/>) or that the ground going back up
/// would bury (<see cref="ObjectsBuriedByGroundGoingUp"/>), under navmeshes on the ground as it is, under the targets and placed
/// records going back in their present shape that end present, under refused placed records that stand now and reach the ground and in
/// cells no longer storable are protected; the heights, quarter textures and water levels whose going back the shared changed-terrain
/// rule counts for a target or copy that stands, judged on the plans as the next patcher reads them (<see cref="StandingTerrainChecks"/>),
/// are kept; and the water levels with a navmesh that stays changed, a refused placed record or a copy at their water line are kept.
/// Every pass reads only the state before it and checks everything again.
/// </summary>
/// <param name="placedGuards">Null when no placed record's areas let it go back.</param>
/// <param name="terrainGuards">Null while the terrain is switched off.</param>
/// <param name="clock">The run's clock, which times the placed guards' checks and the too-close check of the standing objects alone.</param>
internal sealed class RestoreLoop(PlacedGuards? placedGuards, TerrainGuards? terrainGuards, RestoreLoopInputs inputs, Func<TimeSpan> clock)
{
    /// <returns>
    /// The state once a pass changes nothing, the follow-up of the placed records going back in it, how many passes ran, that one
    /// included, the time of the placed guards' checks in all of them, and how many objects the changed-terrain rule still finds on
    /// changed terrain in the final plans, where the restore writes nothing of what the check counts.
    /// </returns>
    /// <remarks>
    /// Each pass that does not end adds a refused placed record, a lost copy, a refused navmesh group, a protected grid point, a
    /// quarter whose textures stay, a kept water level or a stopped terrain check, and none is ever taken back, so the passes are at
    /// most one more than the placed records, navmesh groups, grid points, quarters, water cells and checked objects together.
    /// </remarks>
    public (RestoreState Final, RestoreFollowUp FollowUp, int Passes, TimeSpan PlacedGuardsTime, int TerrainFindingsLeft) Run(RestoreState start)
    {
        var state = start;
        var placedGuardsTime = TimeSpan.Zero;
        for (var passes = 1; ; passes++)
        {
            var input = ReadPassInput(state);
            var (next, passPlacedGuardsTime, terrainFindings) = Pass(input);
            placedGuardsTime += passPlacedGuardsTime;
            if (next.Count == state.Count) return (state, input.FollowUp.Written, passes, placedGuardsTime, terrainFindings);
            state = next;
        }
    }

    /// <remarks>
    /// The follow-up is no state: each pass asks it about the candidates still going back on the ground and water the plans leave, and
    /// only what it keeps in place (a refusal, a lost copy or ground kept) joins the state. The last pass reads the final state, so its
    /// follow-up is the one written.
    /// </remarks>
    private PassInput ReadPassInput(RestoreState state)
    {
        var placedGoingBack = state.PlacedGoingBack();
        var withClone = state.GoingBackWithClone();
        var presence = new PresenceInPass(inputs.FinalPresence, placedGoingBack.Contains);
        var land = inputs.LandOf(state.Terrain, state.Water);
        var followUp = placedGuards is null && land.Lowered.IsEmpty ? FollowUpChecks.None() : inputs.FollowUp.Check(placedGoingBack, withClone, presence, land);
        return new PassInput(
            state, placedGoingBack, withClone, presence, land, followUp, ReadWhatStands(placedGoingBack, withClone, presence, followUp.TargetsDecided), ReadNavmeshesAsPlanned(state));
    }

    /// <remarks>A clone always ends present, whatever becomes of its record: it is a new record no order of the run removes.</remarks>
    /// <param name="decidedByFollowUp">The target objects the follow-up removes or restores, which stand nowhere they are checked.</param>
    private WhatStands ReadWhatStands(IReadOnlySet<int> placedGoingBack, IReadOnlySet<int> withClone, PresenceInPass presence, IReadOnlySet<RecordKey> decidedByFollowUp)
    {
        bool Stands(TargetObject placed) => !presence.EndsRemoved(placed.Key) && !decidedByFollowUp.Contains(placed.Key);
        List<TargetObject> targets = [.. inputs.TargetsThatMayStay.Where(Stands)];
        var clones = inputs.World.ClonesOf(withClone).ToList();
        return new(
            [.. inputs.Standing.ThatStay(placedGoingBack, withClone, presence).Where(standing => !decidedByFollowUp.Contains(standing.Key))],
            [.. inputs.Standing.KeptAndClones(placedGoingBack, withClone, presence).Where(standing => !decidedByFollowUp.Contains(standing.Key))],
            [.. targets, .. inputs.World.NowOfThoseKeepingTheirShape(placedGoingBack).Where(Stands), .. clones],
            [.. targets, .. clones],
            [.. inputs.World.NowOfAllBut(placedGoingBack).Where(Stands)]);
    }

    private NavmeshesAsPlanned ReadNavmeshesAsPlanned(RestoreState state) =>
        new(
            NavmeshesThatStayChanged(state),
            state.Navmeshes?.IndexOfTrianglesGoingBack() ?? new NavmeshIndex(_ => null),
            state.Navmeshes?.MembersGoingBack() ?? new HashSet<RecordKey>());

    /// <returns>Also how many objects have a changed-terrain finding that counts in the plans before the pass.</returns>
    private (RestoreState State, TimeSpan PlacedGuardsTime, int TerrainFindings) Pass(PassInput before)
    {
        var (standing, standingTime) = Timed.Run(clock, () => CheckStandingTooClose(before));
        var (placed, clones, placedGuardsTime) = RefusePlaced(before, standing);
        var counted = FindCountedTerrain(before);
        var state = new RestoreState(
            placed, clones, RefuseNavmeshGroups(before), ProtectGround(before, counted, standing.GroundKeptUnder), KeepWater(before, counted), AddStops(before.State, counted));
        return (state, standingTime + placedGuardsTime, counted.Findings);
    }

    /// <summary>
    /// The too-close check of the cleaned mod's objects that stand, with the placed records going back and where the follow-up pulls
    /// objects down; with no placed record going back, it finds something only where the follow-up pulls an object down.
    /// </summary>
    private StandingTooCloseFound CheckStandingTooClose(PassInput before) =>
        placedGuards is null && before.FollowUp.PulledDown().Count == 0
            ? StandingTooCloseFound.None()
            : inputs.StandingTooClose.Value.Check(before.PlacedGoingBack, before.WithClone, before.PassPresence, before.FollowUp);

    private static ImmutableDictionary<TerrainChecked, PartFailure> AddStops(RestoreState before, TerrainKeeping counted)
    {
        var stops = before.TerrainCheckStops.ToBuilder();
        foreach (var (stopped, failure) in counted.Stops) stops.TryAdd(stopped, failure);
        return stops.ToImmutable();
    }

    /// <summary>What the changed-terrain rule counts in the plans before the pass for the objects a later run judges where they stand.</summary>
    /// <remarks>While the terrain is switched off, no LAND record is judged and none is written.</remarks>
    private TerrainKeeping FindCountedTerrain(PassInput before) =>
        inputs.TerrainChecks.Find(before.Standing.Judged(), terrainGuards is null ? [] : before.State.Terrain.Judged(), before.State.Water.Judged());

    /// <summary>The placed guards and the copies' rules against the current plans, for the placed records still going back, and the time of their checks.</summary>
    /// <param name="standing">What the too-close check of the cleaned mod's objects that stand found in the pass.</param>
    private (ImmutableArray<PlacedRefusal?> Placed, ImmutableArray<CloneJudged?> Clones, TimeSpan Time) RefusePlaced(PassInput before, StandingTooCloseFound standing)
    {
        var state = before.State;
        if (placedGuards is null) return (state.Placed, state.Clones, TimeSpan.Zero);
        var plans = new OtherKindsAsPlanned(before.Navmeshes.ThatStayChanged, before.Navmeshes.GoingBack);
        var (checks, time) = Timed.Run(clock, () => placedGuards.CheckGoingBack(before.PlacedGoingBack, before.WithClone, plans, before.PassPresence, before.FollowUp, standing));
        var (placed, clones) = checks.AppliedTo(state);
        return (placed, clones, time);
    }

    /// <summary>
    /// The ground guard against the current terrain plan, the object guard against the objects that stay, and the water guard and the
    /// door guard with the doors as they end against the current plans, for the groups still going back.
    /// </summary>
    private NavmeshUndoPlan? RefuseNavmeshGroups(PassInput before)
    {
        var state = before.State;
        return state.Navmeshes?
            .RefuseOnGround(inputs.FindLandWorldspace, cell => inputs.GroundBeforeAndAfter(state.Terrain, cell), inputs.HeightTolerance)
            .RefuseOnObjects(before.Standing.ObjectsThatStay, before.Navmeshes.TrianglesGoingBack)
            .RefuseOnWater(state.Water.EndsAsBefore)
            .RefuseOnDoors(new DoorsAsTheyEnd(before.PassPresence, inputs.Doors));
    }

    /// <summary>
    /// The points under the targets and the placed records going back that keep their shape where they stand now, that end present
    /// given the refusals so far and reach the ground, and under refused placed records that stand now, where they stand and reach the
    /// ground; then under the objects of plugins trusted in full the ground going down would leave without support with
    /// KeepSupportingObject or pull down too close to what stays, or the ground going back up would bury, under navmeshes on the ground as
    /// it is, in the cells no longer storable and where the changed-terrain rule counts their going back, each reason kept first; and the
    /// quarters whose textures going back it counts.
    /// </summary>
    /// <param name="groundKeptUnder">The objects the ground going down would pull down too close to what stays, where they stand now.</param>
    private TerrainUndoPlan ProtectGround(PassInput before, TerrainKeeping counted, IReadOnlyList<OtherObject> groundKeptUnder)
    {
        var terrain = before.State.Terrain;
        if (terrainGuards is null) return terrain;
        var standing = before.Standing;
        return terrain
            .Protect(TerrainKeptReason.UnderObjectThatStays, inputs.Ground.UnderWhereTheyReachTheGround(standing.StandingInTheirPlace, terrain.CurrentHeightsOf))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, inputs.Ground.UnderWhereTheyReachTheGround(standing.RefusedThatStand, terrain.CurrentHeightsOf))
            .Protect(
                TerrainKeptReason.UnderOtherModsObject,
                before.FollowUp.GroundHolds.Concat(groundKeptUnder).Concat(inputs.BuriedByGroundGoingUp.On(before.Land.Planned)).SelectMany(inputs.Ground.Under))
            .Protect(TerrainKeptReason.UnderNavmesh, terrainGuards.UnderNavmeshesOnTheGroundAsItIs(terrain, ReadNavmeshesAsTheyWillStand(before)))
            .Protect(TerrainKeptReason.TooSteepToStore, terrain.PointsOfCellsTooSteepToStore())
            .Protect(TerrainKeptReason.ForChangedTerrainCheck, counted.Heights)
            .KeepTextures(counted.Textures);
    }

    private NavmeshesAsTheyWillStand ReadNavmeshesAsTheyWillStand(PassInput before) =>
        before.State.Navmeshes?.AsTheyWillStand(inputs.Winning, before.Navmeshes.TrianglesGoingBack) ?? new NavmeshesAsTheyWillStand(inputs.Winning, _ => true, goingBack: null);

    /// <summary>
    /// The water guard against the triangles of the navmeshes that stay changed and against the refused placed records and copies that
    /// stand where the cleaned mod put them, then the water levels whose going back the changed-terrain rule counts.
    /// </summary>
    private WaterUndoPlan KeepWater(PassInput before, TerrainKeeping counted) =>
        before.State.Water
            .KeepAtNavmeshWaterLines(TrianglesOf(before.Navmeshes.ThatStayChanged))
            .KeepAtObjectWaterLines(before.Standing.KeptAndClones)
            .KeepCounted(counted.Water);

    private List<NavmeshTriangleInCell> TrianglesOf(ImmutableArray<RecordKey> navmeshes)
    {
        var stayChanged = navmeshes.ToHashSet();
        return [.. inputs.ChangedNavmeshTriangles.Where(triangle => stayChanged.Contains(triangle.Navmesh))];
    }

    /// <summary>A navmesh whose switch is off stays changed.</summary>
    private ImmutableArray<RecordKey> NavmeshesThatStayChanged(RestoreState state) => state.Navmeshes?.NavmeshesThatStayChanged() ?? inputs.ChangedNavmeshes;

    /// <summary>What every guard of one pass reads of the state before it, computed once.</summary>
    /// <param name="WithClone">Those of <paramref name="PlacedGoingBack"/> that go back with a copy.</param>
    /// <param name="PassPresence">Whether a placed record ends removed with <paramref name="PlacedGoingBack"/> going back.</param>
    /// <param name="Land">The ground and water the plans of <paramref name="State"/> leave.</param>
    /// <param name="FollowUp">
    /// The removal steps' follow-up of <paramref name="PlacedGoingBack"/> leaving and of the ground going down; none when no placed
    /// record's areas let it go back and no ground goes down.
    /// </param>
    private sealed record PassInput(
        RestoreState State,
        IReadOnlySet<int> PlacedGoingBack,
        IReadOnlySet<int> WithClone,
        PresenceInPass PassPresence,
        LandAfter Land,
        FollowUpChecks FollowUp,
        WhatStands Standing,
        NavmeshesAsPlanned Navmeshes);

    /// <summary>What stands in the final world, given which placed records go back.</summary>
    /// <param name="ObjectsThatStay">In key order.</param>
    /// <param name="KeptAndClones">
    /// The refused placed records and the copies, which stand where the cleaned mod put them, in key order.
    /// </param>
    /// <param name="StandingInTheirPlace">
    /// The targets that may stay and the placed records going back that keep their shape where they stand now, each where it stands,
    /// that end present, then the clones of the placed records going back with one.
    /// </param>
    /// <param name="JudgedInTheirPlace">
    /// Those of <paramref name="StandingInTheirPlace"/> that a later run judges again: all but the placed records going back, whose
    /// every change the patch undoes, so a later run judges them as the cleaned mod never changed them.
    /// </param>
    /// <param name="RefusedThatStand">The refused placed records that stand now and end present, where they stand.</param>
    private sealed record WhatStands(
        List<StandingBox> ObjectsThatStay,
        List<StandingBox> KeptAndClones,
        List<TargetObject> StandingInTheirPlace, List<TargetObject> JudgedInTheirPlace, List<TargetObject> RefusedThatStand)
    {
        /// <summary>The objects a later run judges where they stand: those judged in their place, then the refused ones that stand.</summary>
        public IEnumerable<TargetObject> Judged() => JudgedInTheirPlace.Concat(RefusedThatStand);
    }

    /// <summary>The navmeshes as planned, given which navmesh groups are refused.</summary>
    /// <param name="TrianglesGoingBack">The earlier triangles of the navmesh groups going back; none while the navmeshes are switched off.</param>
    /// <param name="GoingBack">The members of the navmesh groups going back; none while the navmeshes are switched off.</param>
    private sealed record NavmeshesAsPlanned(ImmutableArray<RecordKey> ThatStayChanged, NavmeshIndex TrianglesGoingBack, IReadOnlySet<RecordKey> GoingBack);
}
