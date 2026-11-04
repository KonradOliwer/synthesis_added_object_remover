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
        var withClone = state.GoingBackWithClone();
        var navmeshes = state.Navmeshes;
        var standing = inputs.Standing.ThatStay(placedGoingBack, withClone);
        var trianglesGoingBack = navmeshes?.IndexOfTrianglesGoingBack() ?? new NavmeshIndex(_ => null);
        var thatStayChanged = navmeshes?.NavmeshesThatStayChanged() ?? inputs.ChangedNavmeshes;
        var (placed, clones, causesLeftOut) = RefusePlaced(state, placedGoingBack, withClone, thatStayChanged);
        List<TargetObject> targets = [.. inputs.TargetsThatMayStay.Where(placed => !EndsRemoved(placed.Key, placedGoingBack))];
        List<TargetObject> copies = [.. inputs.World.ClonesOf(withClone)];
        List<TargetObject> standingInTheirPlace = [.. targets, .. inputs.World.NowOfThoseKeepingTheirShape(placedGoingBack).Where(placed => !EndsRemoved(placed.Key, placedGoingBack)), .. copies];
        var refusedThatStand = inputs.World.NowOfAllBut(placedGoingBack).Where(refused => !EndsRemoved(refused.Key, placedGoingBack)).ToList();
        List<TargetObject> judged = [.. targets, .. copies, .. refusedThatStand];
        return new(
            placed,
            clones,
            RefuseNavmeshGroups(state, placedGoingBack, standing, trianglesGoingBack),
            ProtectGround(state, placedGoingBack, standingInTheirPlace, [.. targets, .. copies], refusedThatStand, trianglesGoingBack),
            state.Water.KeepAtWaterLines(standing, TrianglesOf(thatStayChanged)).KeepAroundObjectsThatStay(inputs.StepAreas.WaterAreasAround(judged, state.Terrain), inputs.IsAccepted),
            causesLeftOut);
    }

    private (ImmutableArray<PlacedRefusal?> Placed, ImmutableArray<CloneJudged?> Clones, IReadOnlySet<OtherId>? CausesLeftOut) RefusePlaced(
        RestoreState state, IReadOnlySet<int> placedGoingBack, IReadOnlySet<int> withClone, ImmutableArray<RecordKey> navmeshesThatStayChanged)
    {
        if (placedGuards is null) return (state.Placed, state.Clones, null);
        var goingBack = state.Navmeshes?.MembersGoingBack() ?? new HashSet<RecordKey>();
        var plans = new OtherKindsAsPlanned(state.Terrain, state.Water, navmeshesThatStayChanged, goingBack);
        var checks = placedGuards.CheckGoingBack(placedGoingBack, withClone, plans, state.CausesLeftOut);
        var (placed, clones) = checks.AppliedTo(state);
        return (placed, clones, checks.CausesLeftOut);
    }

    private NavmeshUndoPlan? RefuseNavmeshGroups(RestoreState state, IReadOnlySet<int> placedGoingBack, List<StandingBox> standing, NavmeshIndex trianglesGoingBack) =>
        state.Navmeshes?
            .RefuseOnGround(inputs.FindLandWorldspace, cell => inputs.GroundBeforeAndAfter(state.Terrain, cell), inputs.HeightTolerance)
            .RefuseOnObjects(standing, trianglesGoingBack)
            .RefuseOnWater(state.Water.EndsAsBefore)
            .RefuseOnDoors(new DoorsAsTheyEnd(inputs.FinalPresence, inputs.Doors, placedGoingBack.Contains));

    private TerrainUndoPlan ProtectGround(
        RestoreState state,
        IReadOnlySet<int> placedGoingBack,
        List<TargetObject> standingInTheirPlace,
        List<TargetObject> judgedInTheirPlace,
        List<TargetObject> refusedThatStand,
        NavmeshIndex trianglesGoingBack)
    {
        var terrain = state.Terrain;
        if (terrainGuards is null) return terrain;
        var navmeshesAsTheyWillStand = state.Navmeshes?.AsTheyWillStand(inputs.Winning, trianglesGoingBack) ?? new NavmeshesAsTheyWillStand(inputs.Winning, _ => true, goingBack: null);
        return terrain
            .Protect(TerrainKeptReason.UnderObjectThatStays, inputs.Ground.UnderWhereTheyReachTheGround(standingInTheirPlace, terrain.CurrentHeightsOf))
            .Protect(TerrainKeptReason.UnderObjectThatStays, inputs.StepAreas.Under(judgedInTheirPlace, terrain))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, inputs.Ground.UnderWhereTheyReachTheGround(refusedThatStand, terrain.CurrentHeightsOf))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, inputs.StepAreas.Under(refusedThatStand, terrain))
            .Protect(TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs, terrainGuards.UnderObjectsPlacedForTheGroundAsItIs(terrain, placed => EndsRemoved(placed, placedGoingBack)))
            .Protect(TerrainKeptReason.UnderNavmesh, terrainGuards.UnderNavmeshesOnTheGroundAsItIs(terrain, navmeshesAsTheyWillStand))
            .Protect(TerrainKeptReason.TooSteepToStore, terrain.PointsOfCellsTooSteepToStore())
            .KeepTextures(inputs.StepAreas.QuartersAround(judgedInTheirPlace.Concat(refusedThatStand), terrain));
    }

    private List<NavmeshTriangleInCell> TrianglesOf(ImmutableArray<RecordKey> navmeshes) =>
        [.. inputs.ChangedNavmeshTriangles.Where(triangle => navmeshes.Contains(triangle.Navmesh))];

    private bool EndsRemoved(RecordKey placed, IReadOnlySet<int> placedGoingBack) => inputs.FinalPresence.EndsRemoved(placed, placedGoingBack.Contains);
}
