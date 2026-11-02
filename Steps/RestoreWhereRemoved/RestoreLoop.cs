using System.Collections.Immutable;
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

    /// <summary>How many refusals, protected points and kept water levels it holds; since none is taken back, an equal count means nothing changed.</summary>
    public int Count => Placed.Count(refusal => refusal is not null) + (Navmeshes?.RefusedCount ?? 0) + Terrain.ProtectedCount + Water.KeptCount;
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
/// targets that end present, under refused placed records that stand now and reach the ground and in cells no longer storable are
/// protected; and the water levels with something at their water line are kept. Every pass reads only the state before it.
/// </summary>
/// <param name="placedGuards">Null when no placed record's areas let it go back.</param>
/// <param name="terrainGuards">Null while the terrain is switched off.</param>
internal sealed class RestoreLoop(PlacedGuards? placedGuards, TerrainGuards? terrainGuards, RestoreLoopInputs inputs)
{
    /// <returns>The state once a pass changes nothing, and how many passes ran, that one included.</returns>
    /// <remarks>
    /// Each pass that does not end adds a refused placed record, a refused navmesh group, a protected grid point or a kept water level,
    /// and none is ever taken back, so the passes are at most one more than the placed records, navmesh groups, grid points and water
    /// cells together.
    /// </remarks>
    public (RestoreState Final, int Passes) Run(RestoreState start)
    {
        var state = start;
        for (var passes = 1; ; passes++)
        {
            var next = Pass(state);
            if (next.Count == state.Count) return (state, passes);
            state = next;
        }
    }

    private RestoreState Pass(RestoreState state)
    {
        var before = new PassInput(
            state,
            state.PlacedGoingBack(),
            NavmeshesThatStayChanged(state),
            state.Navmeshes?.IndexOfTrianglesGoingBack() ?? new NavmeshIndex(_ => null),
            state.Navmeshes?.MembersGoingBack() ?? new HashSet<RecordKey>());
        var objectsThatStay = inputs.Standing.ThatStay(before.PlacedGoingBack);
        var (placed, causesLeftOut) = RefusePlaced(before);
        return new(placed, RefuseNavmeshGroups(before, objectsThatStay), ProtectGround(before), KeepWater(before, objectsThatStay), causesLeftOut);
    }

    /// <summary>The placed guards against the current plans, for the placed records still going back.</summary>
    private (ImmutableArray<PlacedRefusal?> Placed, IReadOnlySet<OtherId>? CausesLeftOut) RefusePlaced(PassInput before)
    {
        var state = before.State;
        if (placedGuards is null) return (state.Placed, null);
        var plans = new OtherKindsAsPlanned(state.Terrain, state.Water, before.NavmeshesThatStayChanged, before.NavmeshesGoingBack);
        var checks = placedGuards.CheckGoingBack(before.PlacedGoingBack, plans, state.CausesLeftOut);
        return ([.. state.Placed.Select((refusal, index) => refusal ?? checks.Found[index])], checks.CausesLeftOut);
    }

    /// <summary>The ground, object and water guards, and the door guard with the doors as they end, against the current plans, for the groups still going back.</summary>
    /// <param name="objectsThatStay">In key order.</param>
    private NavmeshUndoPlan? RefuseNavmeshGroups(PassInput before, IReadOnlyList<StandingBox> objectsThatStay)
    {
        var state = before.State;
        return state.Navmeshes?
            .RefuseOnGround(inputs.FindLandWorldspace, cell => inputs.GroundBeforeAndAfter(state.Terrain, cell), inputs.HeightTolerance)
            .RefuseOnObjects(objectsThatStay, before.TrianglesGoingBack)
            .RefuseOnWater(state.Water.EndsAsBefore)
            .RefuseOnDoors(new DoorsAsTheyEnd(inputs.FinalPresence, inputs.Doors, before.PlacedGoingBack.Contains));
    }

    /// <summary>
    /// The points under the targets that end present given the refusals so far and reach the ground, under refused placed records
    /// that stand now, where they stand and reach the ground, then under objects of other plugins placed for the ground as it is,
    /// under navmeshes on the ground as it is and in the cells no longer storable, each reason kept first.
    /// </summary>
    private TerrainUndoPlan ProtectGround(PassInput before)
    {
        var state = before.State;
        if (terrainGuards is null) return state.Terrain;
        var terrain = state.Terrain;
        var underTargets = inputs.Ground.UnderWhereTheyReachTheGround(
            inputs.TargetsThatMayStay.Where(target => !EndsRemoved(target.Key, before)), terrain.CurrentHeightsOf);
        var underRefused = inputs.Ground.UnderWhereTheyReachTheGround(
            inputs.World.NowOfAllBut(before.PlacedGoingBack).Where(refused => !EndsRemoved(refused.Key, before)), terrain.CurrentHeightsOf);
        var navmeshes = state.Navmeshes?.AsTheyWillStand(inputs.Winning, before.TrianglesGoingBack) ?? new NavmeshesAsTheyWillStand(inputs.Winning, _ => true, goingBack: null);
        return terrain
            .Protect(TerrainKeptReason.UnderObjectThatStays, underTargets)
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, underRefused)
            .Protect(TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs, terrainGuards.UnderObjectsPlacedForTheGroundAsItIs(terrain, placed => EndsRemoved(placed, before)))
            .Protect(TerrainKeptReason.UnderNavmesh, terrainGuards.UnderNavmeshesOnTheGroundAsItIs(terrain, navmeshes))
            .Protect(TerrainKeptReason.TooSteepToStore, terrain.PointsOfCellsTooSteepToStore());
    }

    /// <summary>W against the current plans.</summary>
    /// <param name="objectsThatStay">In key order.</param>
    private WaterUndoPlan KeepWater(PassInput before, IReadOnlyList<StandingBox> objectsThatStay)
    {
        var staysChanged = before.NavmeshesThatStayChanged.ToHashSet();
        return before.State.Water.KeepAtWaterLines(objectsThatStay, [.. inputs.ChangedNavmeshTriangles.Where(triangle => staysChanged.Contains(triangle.Navmesh))]);
    }

    private bool EndsRemoved(RecordKey placed, PassInput before) => inputs.FinalPresence.EndsRemoved(placed, before.PlacedGoingBack.Contains);

    /// <summary>A navmesh whose switch is off stays changed.</summary>
    private ImmutableArray<RecordKey> NavmeshesThatStayChanged(RestoreState state) => state.Navmeshes?.NavmeshesThatStayChanged() ?? inputs.ChangedNavmeshes;

    /// <summary>What every guard of one pass reads of the state before it, computed once.</summary>
    /// <param name="TrianglesGoingBack">The earlier triangles of the navmesh groups going back; none while the navmeshes are switched off.</param>
    /// <param name="NavmeshesGoingBack">The members of the navmesh groups going back; none while the navmeshes are switched off.</param>
    private sealed record PassInput(
        RestoreState State,
        IReadOnlySet<int> PlacedGoingBack,
        ImmutableArray<RecordKey> NavmeshesThatStayChanged,
        NavmeshIndex TrianglesGoingBack,
        IReadOnlySet<RecordKey> NavmeshesGoingBack);
}
