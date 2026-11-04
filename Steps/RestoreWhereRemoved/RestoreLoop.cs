using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the guards between the kinds have refused, protected and kept so far. Each part only ever grows.</summary>
/// <param name="Placed">The first refusal of each placed record whose areas let it go back, by its index among them; null for one that goes back.</param>
/// <param name="Navmeshes">Null while the navmeshes are switched off.</param>
/// <param name="Terrain">While the terrain is switched off, a plan in which nothing goes back.</param>
/// <param name="CausesLeftOut">The causes the too-close rule left out in the pass that gave this state; null before the first pass.</param>
internal sealed record RestoreState(
    ImmutableArray<PlacedRefusal?> Placed, NavmeshUndoPlan? Navmeshes, TerrainUndoPlan Terrain, WaterUndoPlan Water, IReadOnlySet<OtherId>? CausesLeftOut)
{
    /// <summary>The indexes of the placed records that go back.</summary>
    public IReadOnlySet<int> PlacedGoingBack() => Enumerable.Range(0, Placed.Length).Where(index => Placed[index] is null).ToHashSet();

    public int PlacedRefusedCount => Placed.Count(refusal => refusal is not null);

    /// <summary>How many refusals, protected points and kept water levels it holds; since none is taken back, an equal count means nothing changed.</summary>
    public int Count => PlacedRefusedCount + (Navmeshes?.RefusedCount ?? 0) + Terrain.ProtectedCount + Water.KeptCount;
}

/// <summary>What every pass of the loop reads that does not change between passes.</summary>
/// <param name="World">The after-removal world over the placed records whose areas let them go back.</param>
/// <param name="TargetsThatMayStay">The targets that are not removed and not among the placed records whose areas let them go back, where they stand last.</param>
/// <param name="FindLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
/// <param name="GroundBeforeAndAfter">A cell's heights before the cleaned mod and as the given terrain plan ends them.</param>
/// <param name="Winning">The winning navmeshes.</param>
/// <param name="ChangedNavmeshes">Every navmesh whose triangles the cleaned mod changed or added, in key order.</param>
/// <param name="ChangedNavmeshTriangles">The triangles of every navmesh the cleaned mod changed or added as they stand, in key order of their navmeshes.</param>
/// <param name="FinalPresence">Whether a placed record ends removed in the final world, given which placed records go back.</param>
/// <param name="Doors">Where each door's navmesh link points, given which placed records go back.</param>
internal sealed record RestoreLoopInputs(
    AfterRemovalWorld World,
    StandingObjects Standing,
    IReadOnlyList<TargetObject> TargetsThatMayStay,
    GroundUnderObjects Ground,
    TerrainStepAreas StepAreas,
    Func<RecordKey, RecordKey?> FindLandWorldspace,
    Func<TerrainUndoPlan, ExteriorCell, LandHeights> GroundBeforeAndAfter,
    float HeightTolerance,
    INavmeshes Winning,
    ImmutableArray<RecordKey> ChangedNavmeshes,
    ImmutableArray<NavmeshTriangleInCell> ChangedNavmeshTriangles,
    FinalPresence FinalPresence,
    DoorLinks Doors);

/// <summary>
/// Repeats the guards between the kinds until nothing more changes: the placed records failing their guards against the current
/// plans are refused, the navmesh groups failing the ground, object and water guards or the door guard with the doors as they end;
/// the grid points under objects of other plugins placed for the ground as it is, under navmeshes on the ground as it is, under the
/// targets and placed records going back in their present shape that end present, under refused placed records that stand now and reach the ground and in cells no longer storable are
/// protected; and the water levels with something at their water line are kept. Every pass reads only the state before it and what
/// the pass before it read.
/// </summary>
/// <param name="placedGuards">Null when no placed record's areas let it go back.</param>
/// <param name="terrainGuards">Null while the terrain is switched off.</param>
/// <param name="clock">The run's clock, which times the placed guards' checks alone.</param>
internal sealed class RestoreLoop(PlacedGuards? placedGuards, TerrainGuards? terrainGuards, RestoreLoopInputs inputs, Func<TimeSpan> clock)
{
    /// <returns>The state once a pass changes nothing, how many passes ran, that one included, and the time of the placed guards' checks in all of them.</returns>
    /// <remarks>
    /// Each pass that does not end adds a refused placed record, a refused navmesh group, a protected grid point or a kept water level,
    /// and none is ever taken back, so the passes are at most one more than the placed records, navmesh groups, grid points and water
    /// cells together.
    /// </remarks>
    public (RestoreState Final, int Passes, TimeSpan PlacedGuardsTime) Run(RestoreState start)
    {
        var state = start;
        PassInput? previous = null;
        var placedGuardsTime = TimeSpan.Zero;
        for (var passes = 1; ; passes++)
        {
            var before = ReadPassInput(state, previous);
            var (next, passPlacedGuardsTime) = Pass(before, FindAddedSincePassBefore(before, previous));
            placedGuardsTime += passPlacedGuardsTime;
            if (next.Count == state.Count) return (state, passes, placedGuardsTime);
            state = next;
            previous = before;
        }
    }

    /// <param name="previous">What the pass before read; null in the first pass.</param>
    /// <remarks>
    /// What stands follows only from the placed records going back and the navmeshes as planned only from the refused navmesh groups,
    /// so each is the pass before's while no placed record, or no navmesh group, was refused since.
    /// </remarks>
    private PassInput ReadPassInput(RestoreState state, PassInput? previous)
    {
        var placedGoingBack = state.PlacedGoingBack();
        return new PassInput(
            state,
            placedGoingBack,
            PlacedRefusedSince(state, previous) ? ReadWhatStands(placedGoingBack) : previous.Standing,
            NavmeshesRefusedSince(state, previous) ? ReadNavmeshesAsPlanned(state) : previous.Navmeshes);
    }

    private WhatStands ReadWhatStands(IReadOnlySet<int> placedGoingBack) =>
        new(
            inputs.Standing.ThatStay(placedGoingBack),
            terrainGuards is null
                ? []
                : [.. inputs.TargetsThatMayStay.Concat(inputs.World.NowOfThoseKeepingTheirShape(placedGoingBack)).Where(placed => !EndsRemoved(placed.Key, placedGoingBack))],
            terrainGuards is null ? [] : [.. inputs.World.NowOfAllBut(placedGoingBack).Where(refused => !EndsRemoved(refused.Key, placedGoingBack))]);

    private NavmeshesAsPlanned ReadNavmeshesAsPlanned(RestoreState state) =>
        new(
            NavmeshesThatStayChanged(state),
            state.Navmeshes?.IndexOfTrianglesGoingBack() ?? new NavmeshIndex(_ => null),
            state.Navmeshes?.MembersGoingBack() ?? new HashSet<RecordKey>());

    /// <returns>True in the first pass.</returns>
    private static bool PlacedRefusedSince(RestoreState state, [NotNullWhen(false)] PassInput? previous) =>
        previous is null || state.PlacedRefusedCount != previous.State.PlacedRefusedCount;

    /// <returns>True in the first pass.</returns>
    private static bool NavmeshesRefusedSince(RestoreState state, [NotNullWhen(false)] PassInput? previous) =>
        previous is null || state.Navmeshes?.RefusedCount != previous.State.Navmeshes?.RefusedCount;

    /// <returns>True in the first pass.</returns>
    private static bool GroundProtectedSince(RestoreState state, PassInput? previous) =>
        previous is null || state.Terrain.ProtectedCount != previous.State.Terrain.ProtectedCount;

    /// <summary>
    /// What the pass reads that the pass before did not, in the pass's order, and which plans changed since; everything in the first
    /// pass. Only this can refuse, protect or keep more: the navmesh groups and water cells going back only shrink, each check the pass
    /// before made reads only its own group, cell or object and what both passes read, and the ground guard of the navmeshes reads
    /// only the protected points.
    /// </summary>
    private static AddedSincePassBefore FindAddedSincePassBefore(PassInput before, PassInput? previous)
    {
        var placedRefused = PlacedRefusedSince(before.State, previous);
        var navmeshesRefused = NavmeshesRefusedSince(before.State, previous);
        return new(
            placedRefused ? before.Standing.FindStandingNotIn(previous?.Standing) : WhatStands.Nothing,
            navmeshesRefused ? before.Navmeshes.FindStayingChangedNotIn(previous?.Navmeshes) : new HashSet<RecordKey>(),
            GroundProtectedSince(before.State, previous),
            placedRefused,
            navmeshesRefused);
    }

    private (RestoreState State, TimeSpan PlacedGuardsTime) Pass(PassInput before, AddedSincePassBefore added)
    {
        var (placed, causesLeftOut, placedGuardsTime) = RefusePlaced(before);
        return (new RestoreState(placed, RefuseNavmeshGroups(before, added), ProtectGround(before, added), KeepWater(before, added), causesLeftOut), placedGuardsTime);
    }

    /// <summary>The placed guards against the current plans, for the placed records still going back, with the time of their checks.</summary>
    private (ImmutableArray<PlacedRefusal?> Placed, IReadOnlySet<OtherId>? CausesLeftOut, TimeSpan Time) RefusePlaced(PassInput before)
    {
        var state = before.State;
        if (placedGuards is null) return (state.Placed, null, TimeSpan.Zero);
        var plans = new OtherKindsAsPlanned(state.Terrain, state.Water, before.Navmeshes.ThatStayChanged, before.Navmeshes.GoingBack);
        var (checks, time) = Timed.Run(clock, () => placedGuards.CheckGoingBack(before.PlacedGoingBack, plans, state.CausesLeftOut));
        return ([.. state.Placed.Select((refusal, index) => refusal ?? checks.Found[index])], checks.CausesLeftOut, time);
    }

    /// <summary>
    /// The ground guard against the current terrain plan when it changed since the pass before, the object guard against the objects
    /// that stay now and did not before, and the water guard and the door guard with the doors as they end against the current plans,
    /// for the groups still going back.
    /// </summary>
    private NavmeshUndoPlan? RefuseNavmeshGroups(PassInput before, AddedSincePassBefore added)
    {
        var state = before.State;
        if (state.Navmeshes is not { } navmeshes) return null;
        var onGround = added.GroundProtected
            ? navmeshes.RefuseOnGround(inputs.FindLandWorldspace, cell => inputs.GroundBeforeAndAfter(state.Terrain, cell), inputs.HeightTolerance)
            : navmeshes;
        var onObjects = added.Standing.ObjectsThatStay.Count == 0
            ? onGround
            : onGround.RefuseOnObjects(added.Standing.ObjectsThatStay, before.Navmeshes.TrianglesGoingBack);
        return onObjects
            .RefuseOnWater(state.Water.EndsAsBefore)
            .RefuseOnDoors(new DoorsAsTheyEnd(inputs.FinalPresence, inputs.Doors, before.PlacedGoingBack.Contains));
    }

    /// <summary>
    /// The points under the targets and the placed records going back that keep their shape where they stand now, that end present
    /// given the refusals so far and reach the ground, and in the areas the changed-terrain step checks around those that are targets
    /// (<see cref="TerrainStepAreas"/>), under refused placed records
    /// that stand now, where they stand and reach the ground, each of those only when it did not in the pass before; then under
    /// objects of other plugins placed for the ground as it is, under navmeshes on the ground as it is and in the cells no longer
    /// storable, each only when what it reads changed since the pass before, and each reason kept first.
    /// </summary>
    /// <remarks>
    /// The points under an object do not depend on the plan, so one that stood in the pass before has all of them protected already.
    /// The areas the changed-terrain step checks around a target do, through the planned heights, so they are read again for every
    /// object that stands whenever the plan protects more.
    /// </remarks>
    private TerrainUndoPlan ProtectGround(PassInput before, AddedSincePassBefore added)
    {
        var state = before.State;
        if (terrainGuards is null) return state.Terrain;
        var terrain = state.Terrain;
        var stepAreasFor = added.GroundProtected ? before.Standing : added.Standing;
        return terrain
            .Protect(TerrainKeptReason.UnderObjectThatStays, UnderAndAround(added.Standing.StandingInTheirPlace, stepAreasFor.StandingInTheirPlace, terrain))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, UnderAndAround(added.Standing.RefusedThatStand, stepAreasFor.RefusedThatStand, terrain))
            .Protect(
                TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs,
                added.GroundProtected || added.PlacedRefused ? terrainGuards.UnderObjectsPlacedForTheGroundAsItIs(terrain, placed => EndsRemoved(placed, before.PlacedGoingBack)) : [])
            .Protect(
                TerrainKeptReason.UnderNavmesh,
                added.GroundProtected || added.NavmeshesRefused ? terrainGuards.UnderNavmeshesOnTheGroundAsItIs(terrain, ReadNavmeshesAsTheyWillStand(before)) : [])
            .Protect(TerrainKeptReason.TooSteepToStore, added.GroundProtected ? terrain.PointsOfCellsTooSteepToStore() : []);
    }

    /// <summary>The points under the first objects where they reach the ground, then in the areas the changed-terrain step checks around the second.</summary>
    private IEnumerable<LandGridPoint> UnderAndAround(IEnumerable<TargetObject> under, IEnumerable<TargetObject> around, TerrainUndoPlan terrain) =>
        inputs.Ground.UnderWhereTheyReachTheGround(under, terrain.CurrentHeightsOf).Concat(inputs.StepAreas.Under(around, terrain));

    private NavmeshesAsTheyWillStand ReadNavmeshesAsTheyWillStand(PassInput before) =>
        before.State.Navmeshes?.AsTheyWillStand(inputs.Winning, before.Navmeshes.TrianglesGoingBack) ?? new NavmeshesAsTheyWillStand(inputs.Winning, _ => true, goingBack: null);

    /// <summary>The water guard against the objects that stay and the triangles of the navmeshes that stay changed now and did not in the pass before.</summary>
    private WaterUndoPlan KeepWater(PassInput before, AddedSincePassBefore added) =>
        added.Standing.ObjectsThatStay.Count == 0 && added.NavmeshesThatStayChanged.Count == 0
            ? before.State.Water
            : before.State.Water.KeepAtWaterLines(added.Standing.ObjectsThatStay, TrianglesOf(added.NavmeshesThatStayChanged));

    private List<NavmeshTriangleInCell> TrianglesOf(IReadOnlySet<RecordKey> navmeshes) =>
        navmeshes.Count == 0 ? [] : [.. inputs.ChangedNavmeshTriangles.Where(triangle => navmeshes.Contains(triangle.Navmesh))];

    private bool EndsRemoved(RecordKey placed, IReadOnlySet<int> placedGoingBack) => inputs.FinalPresence.EndsRemoved(placed, placedGoingBack.Contains);

    /// <summary>A navmesh whose switch is off stays changed.</summary>
    private ImmutableArray<RecordKey> NavmeshesThatStayChanged(RestoreState state) => state.Navmeshes?.NavmeshesThatStayChanged() ?? inputs.ChangedNavmeshes;

    /// <summary>What every guard of one pass reads of the state before it, computed once.</summary>
    private sealed record PassInput(RestoreState State, IReadOnlySet<int> PlacedGoingBack, WhatStands Standing, NavmeshesAsPlanned Navmeshes);

    /// <summary>What stands in the final world, given which placed records go back.</summary>
    /// <param name="ObjectsThatStay">In key order.</param>
    /// <param name="StandingInTheirPlace">
    /// The targets that may stay and the placed records going back that keep their shape where they stand now, each where it stands,
    /// that end present; none while the terrain is switched off.
    /// </param>
    /// <param name="RefusedThatStand">The refused placed records that stand now and end present, where they stand; none while the terrain is switched off.</param>
    private sealed record WhatStands(List<StandingBox> ObjectsThatStay, List<TargetObject> StandingInTheirPlace, List<TargetObject> RefusedThatStand)
    {
        public static WhatStands Nothing => new([], [], []);

        /// <returns>What stands here and not in <paramref name="earlier"/>, each in its order here; all of it when there is no earlier.</returns>
        public WhatStands FindStandingNotIn(WhatStands? earlier)
        {
            if (earlier is null) return this;
            var objects = earlier.ObjectsThatStay.ToHashSet();
            var inTheirPlace = earlier.StandingInTheirPlace.Select(placed => placed.Key).ToHashSet();
            var refused = earlier.RefusedThatStand.Select(candidate => candidate.Key).ToHashSet();
            return new(
                [.. ObjectsThatStay.Where(box => !objects.Contains(box))],
                [.. StandingInTheirPlace.Where(placed => !inTheirPlace.Contains(placed.Key))],
                [.. RefusedThatStand.Where(candidate => !refused.Contains(candidate.Key))]);
        }
    }

    /// <summary>The navmeshes as planned, given which navmesh groups are refused.</summary>
    /// <param name="TrianglesGoingBack">The earlier triangles of the navmesh groups going back; none while the navmeshes are switched off.</param>
    /// <param name="GoingBack">The members of the navmesh groups going back; none while the navmeshes are switched off.</param>
    private sealed record NavmeshesAsPlanned(ImmutableArray<RecordKey> ThatStayChanged, NavmeshIndex TrianglesGoingBack, IReadOnlySet<RecordKey> GoingBack)
    {
        /// <returns>The navmeshes that stay changed here and not in <paramref name="earlier"/>; all of them when there is no earlier.</returns>
        public IReadOnlySet<RecordKey> FindStayingChangedNotIn(NavmeshesAsPlanned? earlier) =>
            earlier is null ? ThatStayChanged.ToHashSet() : ThatStayChanged.Except(earlier.ThatStayChanged).ToHashSet();
    }

    /// <summary>What the pass reads that the pass before did not, and which plans changed since.</summary>
    /// <param name="Standing">What stands now and did not in the pass before, each in its order now.</param>
    /// <param name="GroundProtected">The terrain plan protects more points than the pass before's.</param>
    /// <param name="PlacedRefused">More placed records are refused than in the pass before.</param>
    /// <param name="NavmeshesRefused">More navmesh groups are refused than in the pass before.</param>
    private sealed record AddedSincePassBefore(
        WhatStands Standing, IReadOnlySet<RecordKey> NavmeshesThatStayChanged, bool GroundProtected, bool PlacedRefused, bool NavmeshesRefused);
}
