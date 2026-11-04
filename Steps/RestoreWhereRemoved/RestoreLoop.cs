using System.Collections.Immutable;
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
internal sealed record RestoreState(
    ImmutableArray<PlacedRefusal?> Placed, ImmutableArray<CloneJudged?> Clones, NavmeshUndoPlan? Navmeshes, TerrainUndoPlan Terrain, WaterUndoPlan Water)
{
    /// <summary>The indexes of the placed records that go back, with or without a copy.</summary>
    public IReadOnlySet<int> PlacedGoingBack() => Enumerable.Range(0, Placed.Length).Where(index => Placed[index] is null && Clones[index] is not { RefusedBy: not null }).ToHashSet();

    /// <summary>The indexes of the placed records that go back with a copy.</summary>
    public IReadOnlySet<int> GoingBackWithClone() => Enumerable.Range(0, Placed.Length).Where(index => Placed[index] is null && Clones[index] is { RefusedBy: null }).ToHashSet();

    /// <summary>How many placed records no longer go back: those refused and those whose copy is lost.</summary>
    public int PlacedStoppedCount => Placed.Count(refusal => refusal is not null) + Clones.Count(clone => clone is { RefusedBy: not null });

    /// <summary>How many refusals, lost copies, protected points and kept water levels it holds; since none is taken back, an equal count means nothing changed.</summary>
    public int Count => PlacedStoppedCount + (Navmeshes?.RefusedCount ?? 0) + Terrain.ProtectedCount + Terrain.TexturesKeptCount + Water.KeptCount;
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
/// <param name="IsAccepted">Whether the changed-terrain step accepts the values of a plugin's version whatever the others hold.</param>
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
    DoorLinks Doors,
    Func<PluginName, bool> IsAccepted);

/// <summary>
/// Repeats the guards between the kinds until nothing more changes: the placed records failing their guards against the current
/// plans are refused, and those whose copy fails its rules (<see cref="CloneGuards"/>) stay, the navmesh groups failing the ground,
/// object and water guards or the door guard with the doors as they end;
/// the grid points under objects of other plugins placed for the ground as it is, under navmeshes on the ground as it is, under the
/// targets and placed records going back in their present shape that end present, under refused placed records that stand now and reach the ground and in cells no longer storable are
/// protected; the quarter textures the changed-terrain step reads around the targets and copies that stand are kept; and the water
/// levels with something at their water line, or that the step reads around a target or copy that stands, are kept. Every pass reads
/// only the state before it and checks everything again.
/// </summary>
/// <param name="placedGuards">Null when no placed record's areas let it go back.</param>
/// <param name="terrainGuards">Null while the terrain is switched off.</param>
/// <param name="clock">The run's clock, which times the placed guards' checks alone.</param>
internal sealed class RestoreLoop(PlacedGuards? placedGuards, TerrainGuards? terrainGuards, RestoreLoopInputs inputs, Func<TimeSpan> clock)
{
    /// <returns>The state once a pass changes nothing, how many passes ran, that one included, and the time of the placed guards' checks in all of them.</returns>
    /// <remarks>
    /// Each pass that does not end adds a refused placed record, a lost copy, a refused navmesh group, a protected grid point, a
    /// quarter whose textures stay or a kept water level, and none is ever taken back, so the passes are at most one more than the
    /// placed records, navmesh groups, grid points, quarters and water cells together.
    /// </remarks>
    public (RestoreState Final, int Passes, TimeSpan PlacedGuardsTime) Run(RestoreState start)
    {
        var state = start;
        var placedGuardsTime = TimeSpan.Zero;
        for (var passes = 1; ; passes++)
        {
            var (next, passPlacedGuardsTime) = Pass(ReadPassInput(state));
            placedGuardsTime += passPlacedGuardsTime;
            if (next.Count == state.Count) return (state, passes, placedGuardsTime);
            state = next;
        }
    }

    private PassInput ReadPassInput(RestoreState state)
    {
        var placedGoingBack = state.PlacedGoingBack();
        var withClone = state.GoingBackWithClone();
        var presence = new PresenceInPass(inputs.FinalPresence, placedGoingBack.Contains);
        return new PassInput(state, placedGoingBack, withClone, presence, ReadWhatStands(placedGoingBack, withClone, presence), ReadNavmeshesAsPlanned(state));
    }

    /// <remarks>A clone always ends present, whatever becomes of its record: it is a new record no order of the run removes.</remarks>
    private WhatStands ReadWhatStands(IReadOnlySet<int> placedGoingBack, IReadOnlySet<int> withClone, PresenceInPass presence)
    {
        List<TargetObject> targets = [.. inputs.TargetsThatMayStay.Where(placed => !presence.EndsRemoved(placed.Key))];
        var clones = inputs.World.ClonesOf(withClone).ToList();
        return new(
            inputs.Standing.ThatStay(placedGoingBack, withClone, presence),
            [.. targets, .. inputs.World.NowOfThoseKeepingTheirShape(placedGoingBack).Where(placed => !presence.EndsRemoved(placed.Key)), .. clones],
            [.. targets, .. clones],
            [.. inputs.World.NowOfAllBut(placedGoingBack).Where(refused => !presence.EndsRemoved(refused.Key))]);
    }

    private NavmeshesAsPlanned ReadNavmeshesAsPlanned(RestoreState state) =>
        new(
            NavmeshesThatStayChanged(state),
            state.Navmeshes?.IndexOfTrianglesGoingBack() ?? new NavmeshIndex(_ => null),
            state.Navmeshes?.MembersGoingBack() ?? new HashSet<RecordKey>());

    private (RestoreState State, TimeSpan PlacedGuardsTime) Pass(PassInput before)
    {
        var (placed, clones, placedGuardsTime) = RefusePlaced(before);
        return (new RestoreState(placed, clones, RefuseNavmeshGroups(before), ProtectGround(before), KeepWater(before)), placedGuardsTime);
    }

    /// <summary>
    /// The placed guards and the copies' rules against the current plans, for the placed records still going back, with the time of
    /// their checks.
    /// </summary>
    private (ImmutableArray<PlacedRefusal?> Placed, ImmutableArray<CloneJudged?> Clones, TimeSpan Time) RefusePlaced(PassInput before)
    {
        var state = before.State;
        if (placedGuards is null) return (state.Placed, state.Clones, TimeSpan.Zero);
        var plans = new OtherKindsAsPlanned(state.Terrain, state.Water, before.Navmeshes.ThatStayChanged, before.Navmeshes.GoingBack);
        var (checks, time) = Timed.Run(clock, () => placedGuards.CheckGoingBack(before.PlacedGoingBack, before.WithClone, plans, before.PassPresence));
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
    /// given the refusals so far and reach the ground, and in the areas the changed-terrain step checks around the targets and copies
    /// among them (<see cref="TerrainStepAreas"/>), under refused placed records that stand now, where they stand and reach the ground;
    /// then under objects of other plugins placed for the ground as it is, under navmeshes on the ground as it is and in the cells no
    /// longer storable, each reason kept first.
    /// </summary>
    private TerrainUndoPlan ProtectGround(PassInput before)
    {
        var terrain = before.State.Terrain;
        if (terrainGuards is null) return terrain;
        var standing = before.Standing;
        return terrain
            .Protect(TerrainKeptReason.UnderObjectThatStays, UnderAndAround(standing.StandingInTheirPlace, standing.JudgedInTheirPlace, terrain))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, UnderAndAround(standing.RefusedThatStand, standing.RefusedThatStand, terrain))
            .Protect(TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs, terrainGuards.UnderObjectsPlacedForTheGroundAsItIs(terrain, before.PassPresence.EndsRemoved))
            .Protect(TerrainKeptReason.UnderNavmesh, terrainGuards.UnderNavmeshesOnTheGroundAsItIs(terrain, ReadNavmeshesAsTheyWillStand(before)))
            .Protect(TerrainKeptReason.TooSteepToStore, terrain.PointsOfCellsTooSteepToStore())
            .KeepTextures(inputs.StepAreas.QuartersAround(standing.Judged(), terrain));
    }

    /// <summary>The points under the first objects where they reach the ground, then in the areas the changed-terrain step checks around the second.</summary>
    private IEnumerable<LandGridPoint> UnderAndAround(IEnumerable<TargetObject> under, IEnumerable<TargetObject> around, TerrainUndoPlan terrain) =>
        inputs.Ground.UnderWhereTheyReachTheGround(under, terrain.CurrentHeightsOf).Concat(inputs.StepAreas.Under(around, terrain));

    private NavmeshesAsTheyWillStand ReadNavmeshesAsTheyWillStand(PassInput before) =>
        before.State.Navmeshes?.AsTheyWillStand(inputs.Winning, before.Navmeshes.TrianglesGoingBack) ?? new NavmeshesAsTheyWillStand(inputs.Winning, _ => true, goingBack: null);

    /// <summary>
    /// The water guard against the objects that stay and the triangles of the navmeshes that stay changed, then the water levels the
    /// changed-terrain step counts around the targets and copies that stand.
    /// </summary>
    private WaterUndoPlan KeepWater(PassInput before) =>
        before.State.Water
            .KeepAtWaterLines(before.Standing.ObjectsThatStay, TrianglesOf(before.Navmeshes.ThatStayChanged))
            .KeepAroundObjectsThatStay(inputs.StepAreas.WaterAreasAround(before.Standing.Judged(), before.State.Terrain), inputs.IsAccepted);

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
    private sealed record PassInput(
        RestoreState State, IReadOnlySet<int> PlacedGoingBack, IReadOnlySet<int> WithClone, PresenceInPass PassPresence, WhatStands Standing, NavmeshesAsPlanned Navmeshes);

    /// <summary>What stands in the final world, given which placed records go back.</summary>
    /// <param name="ObjectsThatStay">In key order.</param>
    /// <param name="StandingInTheirPlace">
    /// The targets that may stay and the placed records going back that keep their shape where they stand now, each where it stands,
    /// that end present, then the clones of the placed records going back with one.
    /// </param>
    /// <param name="JudgedInTheirPlace">
    /// Those of <paramref name="StandingInTheirPlace"/> that a later run judges again: all but the placed records going back, whose
    /// every change the patch undoes, so a later run judges them as the cleaned mod never changed them.
    /// </param>
    /// <param name="RefusedThatStand">The refused placed records that stand now and end present, where they stand.</param>
    private sealed record WhatStands(List<StandingBox> ObjectsThatStay, List<TargetObject> StandingInTheirPlace, List<TargetObject> JudgedInTheirPlace, List<TargetObject> RefusedThatStand)
    {
        /// <summary>The objects a later run judges where they stand: those judged in their place, then the refused ones that stand.</summary>
        public IEnumerable<TargetObject> Judged() => JudgedInTheirPlace.Concat(RefusedThatStand);
    }

    /// <summary>The navmeshes as planned, given which navmesh groups are refused.</summary>
    /// <param name="TrianglesGoingBack">The earlier triangles of the navmesh groups going back; none while the navmeshes are switched off.</param>
    /// <param name="GoingBack">The members of the navmesh groups going back; none while the navmeshes are switched off.</param>
    private sealed record NavmeshesAsPlanned(ImmutableArray<RecordKey> ThatStayChanged, NavmeshIndex TrianglesGoingBack, IReadOnlySet<RecordKey> GoingBack);
}
