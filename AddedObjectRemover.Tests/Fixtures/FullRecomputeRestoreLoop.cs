using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// The restore loop with nothing taken over from the pass before: every pass reads what stands and the navmeshes as planned afresh
/// and runs every guard over all of it. It is built from the loop's own guards, so the real loop, which reuses the pass before's work,
/// must end in the same state after the same number of passes.
/// </summary>
internal sealed class FullRecomputeRestoreLoop(PlacedGuards? placedGuards, TerrainGuards? terrainGuards, RestoreLoopInputs inputs)
{
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
        var placedGoingBack = state.PlacedGoingBack();
        var navmeshes = state.Navmeshes;
        var standing = inputs.Standing.ThatStay(placedGoingBack);
        var trianglesGoingBack = navmeshes?.IndexOfTrianglesGoingBack() ?? new NavmeshIndex(_ => null);
        var thatStayChanged = navmeshes?.NavmeshesThatStayChanged() ?? inputs.ChangedNavmeshes;
        var (placed, causesLeftOut) = RefusePlaced(state, placedGoingBack, thatStayChanged);
        return new(
            placed,
            RefuseNavmeshGroups(state, placedGoingBack, standing, trianglesGoingBack),
            ProtectGround(state, placedGoingBack, trianglesGoingBack),
            state.Water.KeepAtWaterLines(standing, TrianglesOf(thatStayChanged)),
            causesLeftOut);
    }

    private (ImmutableArray<PlacedRefusal?> Placed, IReadOnlySet<OtherId>? CausesLeftOut) RefusePlaced(
        RestoreState state, IReadOnlySet<int> placedGoingBack, ImmutableArray<RecordKey> navmeshesThatStayChanged)
    {
        if (placedGuards is null) return (state.Placed, null);
        var goingBack = state.Navmeshes?.MembersGoingBack() ?? new HashSet<RecordKey>();
        var plans = new OtherKindsAsPlanned(state.Terrain, state.Water, navmeshesThatStayChanged, goingBack);
        var checks = placedGuards.CheckGoingBack(placedGoingBack, plans, state.CausesLeftOut);
        return ([.. state.Placed.Select((refusal, index) => refusal ?? checks.Found[index])], checks.CausesLeftOut);
    }

    private NavmeshUndoPlan? RefuseNavmeshGroups(RestoreState state, IReadOnlySet<int> placedGoingBack, List<StandingBox> standing, NavmeshIndex trianglesGoingBack) =>
        state.Navmeshes?
            .RefuseOnGround(inputs.FindLandWorldspace, cell => inputs.GroundBeforeAndAfter(state.Terrain, cell), inputs.HeightTolerance)
            .RefuseOnObjects(standing, trianglesGoingBack)
            .RefuseOnWater(state.Water.EndsAsBefore)
            .RefuseOnDoors(new DoorsAsTheyEnd(inputs.FinalPresence, inputs.Doors, placedGoingBack.Contains));

    private TerrainUndoPlan ProtectGround(RestoreState state, IReadOnlySet<int> placedGoingBack, NavmeshIndex trianglesGoingBack)
    {
        var terrain = state.Terrain;
        if (terrainGuards is null) return terrain;
        var targetsThatStand = inputs.TargetsThatMayStay.Where(target => !EndsRemoved(target.Key, placedGoingBack)).ToList();
        var refusedThatStand = inputs.World.NowOfAllBut(placedGoingBack).Where(refused => !EndsRemoved(refused.Key, placedGoingBack)).ToList();
        var navmeshesAsTheyWillStand = state.Navmeshes?.AsTheyWillStand(inputs.Winning, trianglesGoingBack) ?? new NavmeshesAsTheyWillStand(inputs.Winning, _ => true, goingBack: null);
        return terrain
            .Protect(TerrainKeptReason.UnderObjectThatStays, inputs.Ground.UnderWhereTheyReachTheGround(targetsThatStand, terrain.CurrentHeightsOf))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, inputs.Ground.UnderWhereTheyReachTheGround(refusedThatStand, terrain.CurrentHeightsOf))
            .Protect(TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs, terrainGuards.UnderObjectsPlacedForTheGroundAsItIs(terrain, placed => EndsRemoved(placed, placedGoingBack)))
            .Protect(TerrainKeptReason.UnderNavmesh, terrainGuards.UnderNavmeshesOnTheGroundAsItIs(terrain, navmeshesAsTheyWillStand))
            .Protect(TerrainKeptReason.TooSteepToStore, terrain.PointsOfCellsTooSteepToStore());
    }

    private List<NavmeshTriangleInCell> TrianglesOf(ImmutableArray<RecordKey> navmeshes) =>
        [.. inputs.ChangedNavmeshTriangles.Where(triangle => navmeshes.Contains(triangle.Navmesh))];

    private bool EndsRemoved(RecordKey placed, IReadOnlySet<int> placedGoingBack) => inputs.FinalPresence.EndsRemoved(placed, placedGoingBack.Contains);
}
