using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The plans of the other kinds a placed record going back is checked against.</summary>
/// <param name="NavmeshesThatStayChanged">The navmeshes that will not match the world without the cleaned mod, in key order.</param>
/// <param name="NavmeshesGoingBack">The members of the navmesh groups going back.</param>
internal sealed record OtherKindsAsPlanned(
    TerrainUndoPlan Terrain, WaterUndoPlan Water, ImmutableArray<RecordKey> NavmeshesThatStayChanged, IReadOnlySet<RecordKey> NavmeshesGoingBack);

/// <summary>What one pass of the placed guards found.</summary>
/// <param name="Found">By candidate index; null for one that passes or no longer goes back.</param>
/// <param name="CausesLeftOut">The causes the too-close rules left out for every candidate in the pass, which the next pass compares with its own.</param>
internal sealed record PlacedChecks(PlacedRefusal?[] Found, IReadOnlySet<OtherId> CausesLeftOut);

/// <summary>What the placed guards read besides the after-removal world.</summary>
/// <param name="Caches">The object caches over the after-removal world.</param>
/// <param name="TooClose">The too-close rule over the after-removal world in which the given objects cause no removals.</param>
/// <param name="HoldersOfObjectsThatStay">The support test over the after-removal world, among the candidates that stand now.</param>
/// <param name="FindLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
/// <param name="ChangedTerrain">The areas around an object whose ground the collision settings of the objects the cleaned mod changed look at.</param>
/// <param name="FindTerrainWinner">The plugin of the winning LAND record of a cell; null where the cell has none.</param>
internal sealed record PlacedGuardInputs(
    IObjectCaches Caches,
    Func<IEnumerable<OtherId>, IPlacementRule> TooClose,
    IHoldersSearch HoldersOfObjectsThatStay,
    IBaseObjectShapes Shapes,
    ObjectsThatBlockActors Blocking,
    INavmeshes Navmeshes,
    Func<RecordKey, RecordKey?> FindLandWorldspace,
    ChangedTerrainSettings ChangedTerrain,
    Func<ExteriorCell, PluginName?> FindTerrainWinner);

/// <summary>
/// The guards of the candidates, the placed records whose areas let them go back, each checked as it goes back, with only its undone
/// parts taken from its earlier version. The too-close guard checks a candidate against the causes of <see cref="AfterRemovalWorld"/>,
/// a refused candidate among them where it stands now, less the causes left out:
/// <list type="bullet">
/// <item>for every candidate, the candidates going back, which move away, and every cause that ends removed in the final world
/// (<see cref="FinalPresence"/>);</item>
/// <item>for a candidate that ends in its earlier shape and place (<see cref="AfterRemovalWorld.EndInEarlierShapeAndPlace"/>), also the
/// causes a removal step restored, since the two stood together like that before the cleaned mod. A candidate that keeps a shaping part
/// a later plugin changed never stood beside them like that, so they block it.</item>
/// </list>
/// Thread-safe.
/// </summary>
internal sealed class PlacedGuards(
    AfterRemovalWorld world, PlacedGuardInputs inputs, GuardReach reach, FinalPresence presence, DoorLinkGuard doors, Execution execution)
{
    /// <summary>The invisible-object guard for every candidate: which other candidates go back does not change which invisible object a candidate holds.</summary>
    private readonly ComputedOnce<Dictionary<int, RecordKey>> _holdingInvisible = new(() => FindCandidatesHoldingInvisibleObjects(world, inputs.Caches, inputs.Shapes, execution));

    /// <summary>The door rule's consistency checks, made once, one after another.</summary>
    private readonly ComputedOnce<IReadOnlyDictionary<RecordKey, NavmeshFault?>> _doorNavmeshFaults = new(doors.CheckEachNavmeshWithItsDoors);

    /// <summary>The box around each candidate as it goes back that holds every zone of the too-close rule.</summary>
    private readonly ComputedOnce<Box[]> _tooCloseReach = new(() => [.. world.Restored.Select(reach.TooCloseReachOf)]);

    /// <summary>The first guard each candidate going back fails, with every other one of them going back too.</summary>
    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="causesLeftOutBefore">
    /// What the pass before left out of the causes; null in the first pass. The too-close rule checks again only the candidates
    /// that a cause no longer left out can reach: a candidate it passed before passes again otherwise.
    /// </param>
    public PlacedChecks CheckGoingBack(IReadOnlySet<int> goingBack, OtherKindsAsPlanned plans, IReadOnlySet<OtherId>? causesLeftOutBefore)
    {
        var causesLeftOut = CausesLeftOut(goingBack);
        var rules = CreateTooCloseRules(causesLeftOut, goingBack);
        // Covers the candidates that end in their earlier shape and place too: the causes restored by removal steps they also leave out are the same in every pass.
        var checkTooClose = causesLeftOutBefore is null ? null : CandidatesReaching([.. causesLeftOutBefore.Except(causesLeftOut).OrderBy(cause => cause.Index)], goingBack);
        var holdingInvisible = _holdingInvisible.Value;
        var carrying = FindCandidatesCarryingObjectsThatStay(goingBack);
        var doorNavmeshFaults = _doorNavmeshFaults.Value;
        var staysChanged = plans.NavmeshesThatStayChanged.ToHashSet();
        var found = ParallelMap.Run(
            execution,
            world.Restored.Length,
            candidate => goingBack.Contains(candidate)
                ? FirstRefusal(candidate, checkTooClose?.Contains(candidate) == false ? null : rules.For(candidate), goingBack, plans, staysChanged, holdingInvisible, carrying, doorNavmeshFaults)
                : null,
            ParallelMap.AutomaticRangeSize);
        return new PlacedChecks(found, causesLeftOut);
    }

    /// <summary>The too-close rules of one pass, each made only when a candidate going back needs it.</summary>
    /// <param name="causesLeftOut">The causes left out for every candidate.</param>
    private TooCloseRules CreateTooCloseRules(HashSet<OtherId> causesLeftOut, IReadOnlySet<int> goingBack)
    {
        IPlacementRule? Create(IEnumerable<OtherId> leftOut, bool inEarlierShapeAndPlace) =>
            goingBack.Any(candidate => world.EndInEarlierShapeAndPlace[candidate] == inEarlierShapeAndPlace) ? inputs.TooClose(leftOut.Distinct().OrderBy(cause => cause.Index)) : null;

        return new TooCloseRules(
            world.EndInEarlierShapeAndPlace,
            Create(causesLeftOut.Concat(world.CausesRestoredByRemovalSteps), inEarlierShapeAndPlace: true),
            Create(causesLeftOut, inEarlierShapeAndPlace: false));
    }

    /// <param name="InEarlierShapeAndPlace">For the candidates that end in their earlier shape and place; null when none of them goes back.</param>
    /// <param name="InOtherShapeOrPlace">For the others; null when none of them goes back.</param>
    private sealed record TooCloseRules(ImmutableArray<bool> EndInEarlierShapeAndPlace, IPlacementRule? InEarlierShapeAndPlace, IPlacementRule? InOtherShapeOrPlace)
    {
        public IPlacementRule For(int candidate) =>
            (EndInEarlierShapeAndPlace[candidate] ? InEarlierShapeAndPlace : InOtherShapeOrPlace) ?? throw new UnreachableException($"No too-close rule was made for candidate {candidate}, which goes back.");
    }

    /// <param name="rule">Null when the too-close rule needs no check.</param>
    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="staysChanged">The navmeshes that stay changed.</param>
    /// <param name="holdingInvisible">The candidates holding an invisible object of the cleaned mod, each with the first one.</param>
    /// <param name="carrying">The candidates an object that stays rests on, each with the first one.</param>
    /// <param name="doorNavmeshFaults">What the door rule's consistency checks found, by navmesh.</param>
    private PlacedRefusal? FirstRefusal(
        int candidate,
        IPlacementRule? rule,
        IReadOnlySet<int> goingBack,
        OtherKindsAsPlanned plans,
        IReadOnlySet<RecordKey> staysChanged,
        IReadOnlyDictionary<int, RecordKey> holdingInvisible,
        IReadOnlyDictionary<int, RecordKey> carrying,
        IReadOnlyDictionary<RecordKey, NavmeshFault?> doorNavmeshFaults)
    {
        var restored = world.Restored[candidate];
        return (rule is null ? null : CheckTooClose(restored, rule))
            ?? CheckGround(restored, plans.Terrain)
            ?? CheckWater(restored, plans.Water)
            ?? RefusalFound(PlacedGuard.HoldsInvisibleObjectThatStays, holdingInvisible, candidate)
            ?? CheckNavmeshes(restored, plans.NavmeshesThatStayChanged, staysChanged)
            ?? RefusalFound(PlacedGuard.CarriesObjectThatStays, carrying, candidate)
            ?? doors.Check(candidate, goingBack, plans.NavmeshesGoingBack, doorNavmeshFaults);
    }

    /// <summary>The causes left out for every candidate: the copies of the candidates going back where they stand now, and those that end removed.</summary>
    private HashSet<OtherId> CausesLeftOut(IReadOnlySet<int> goingBack) =>
    [
        .. world.CauseCopiesNowOf(goingBack),
        .. world.World.OtherModObjects.Where(cause => presence.EndsRemoved(cause.Key, goingBack.Contains)).Select(cause => cause.Id),
    ];

    /// <summary>The candidates going back that one of the causes can reach; every one of them when a cause is a placed NPC, whose body is measured on its own.</summary>
    private HashSet<int> CandidatesReaching(List<OtherId> causes, IReadOnlySet<int> goingBack)
    {
        var objects = causes.Select(cause => world.World.OtherModObjects[cause.Index]).ToList();
        if (objects.Any(other => other.IsPlacedNpc)) return [.. goingBack];
        var bounds = objects.Select(other => (other.SpaceKey, Box: reach.BoundsOf(other))).ToList();
        var tooCloseReach = _tooCloseReach.Value;
        return [.. goingBack.Where(candidate => bounds.Any(cause => cause.SpaceKey == world.Restored[candidate].SpaceKey && cause.Box.Overlaps(tooCloseReach[candidate])))];
    }

    /// <summary>
    /// Ground that stays changed lies under the box or an area the changed terrain settings grow around it: ground the terrain plan
    /// keeps as the cleaned mod made it, or ground a later plugin changed. The plan's own blend does not count, so one refusal does
    /// not spread through a cleared area by the slopes around the ground it keeps. The winning LAND record's plugin keeps it changed.
    /// </summary>
    private PlacedRefusal? CheckGround(TargetObject restored, TerrainUndoPlan terrain)
    {
        if (inputs.FindLandWorldspace(restored.SpaceKey) is not { } land) return null;
        var areas = inputs.ChangedTerrain.AreasAround(inputs.Shapes.Of(restored.Base).Box, restored.Transform);
        var found = areas.Prepend(BoxOf(restored))
            .Select(area => terrain.GroundThatStaysChangedUnder(land, (new Vector2(area.Min.X, area.Min.Y), new Vector2(area.Max.X, area.Max.Y))))
            .FirstOrDefault(ground => ground is not null);
        if (found is not { } changed) return null;
        var keptBy = inputs.FindTerrainWinner(changed.Cell)
            ?? throw new UnreachableException($"The ground of cell {changed.Cell} stays changed, but no LAND record of it wins.");
        return new PlacedRefusal(PlacedGuard.OnGroundThatStaysChanged, changed.Land, Failure: null, keptBy);
    }

    /// <summary>The box lies partly between a cell's water level before the cleaned mod and the one that stays.</summary>
    private PlacedRefusal? CheckWater(TargetObject restored, WaterUndoPlan water) =>
        water.CellWithChangedWaterLineIn(restored.SpaceKey, BoxOf(restored)) is { } cell ? new PlacedRefusal(PlacedGuard.AtWaterLineThatStaysChanged, cell, Failure: null) : null;

    private Box BoxOf(TargetObject placed) => BaseBoxes.Of(inputs.Shapes, placed.Base, placed.Transform).WorldAabb(0f);

    private PlacedRefusal? CheckTooClose(TargetObject restored, IPlacementRule rule)
    {
        var check = rule.CheckWhereItStands(restored);
        if (check.Failure is { } failure) return new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, By: null, failure);
        return check.Clash switch
        {
            null => null,
            TooClose clash => new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, world.World.Other(clash.Other).Key, Failure: null),
            var other => throw new UnreachableException($"The too-close rule gave the reason {other}."),
        };
    }

    private static PlacedRefusal? RefusalFound(PlacedGuard guard, IReadOnlyDictionary<int, RecordKey> found, int candidate) =>
        found.TryGetValue(candidate, out var by) ? new PlacedRefusal(guard, by, Failure: null) : null;

    /// <returns>The first navmesh, in key order, that stays changed and reaches into the candidate's box; null when none does or actors would not run into the candidate.</returns>
    private PlacedRefusal? CheckNavmeshes(TargetObject restored, ImmutableArray<RecordKey> navmeshesThatStayChanged, IReadOnlySet<RecordKey> staysChanged)
    {
        if (inputs.Blocking.SolidBoxOf(restored) is not { } box) return null;
        if (!inputs.Navmeshes.AnyTriangleIn(restored.SpaceKey, box, staysChanged.Contains)) return null;

        var first = navmeshesThatStayChanged.First(navmesh => inputs.Navmeshes.AnyTriangleIn(restored.SpaceKey, box, key => key == navmesh));
        return new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, first, Failure: null);
    }

    /// <summary>Every candidate whose mesh holds an invisible object of the cleaned mod that stays, each with the first such object in id order.</summary>
    private static Dictionary<int, RecordKey> FindCandidatesHoldingInvisibleObjects(AfterRemovalWorld world, IObjectCaches caches, IBaseObjectShapes shapes, Execution execution)
    {
        var invisible = world.CleanedModObjectsThatStand.Where(target => !shapes.VisibilityOf(target).IsVisible).ToList();
        var holders = ParallelMap.Run(
            execution,
            invisible.Count,
            () => (Near: new List<VisibleTargetNear>(), Query: new ObjectQueryScratch()),
            (index, scratch) => FindCandidatesHolding(caches, invisible[index], scratch.Near, scratch.Query),
            ParallelMap.AutomaticRangeSize);
        var first = new Dictionary<int, RecordKey>();
        foreach (var (inside, candidates) in invisible.Zip(holders))
        {
            foreach (var candidate in candidates) first.TryAdd(candidate, inside.Key);
        }
        return first;
    }

    /// <returns>The candidates whose box holds the object's position and whose mesh surrounds it, ascending.</returns>
    private static List<int> FindCandidatesHolding(IObjectCaches caches, TargetObject inside, List<VisibleTargetNear> near, ObjectQueryScratch query)
    {
        var position = inside.Transform.Position;
        caches.VisibleTargets.Around(inside.SpaceKey, position, 0f, near);
        return
        [
            .. near.Select(found => found.Target)
                .Where(candidate => caches.VisibleTargets.AnyContains(inside.SpaceKey, position, id => id == candidate, query))
                .Select(candidate => candidate.Index),
        ];
    }

    /// <summary>The candidates going back that an object that stays rests on where they stand now, each with the first such object.</summary>
    private Dictionary<int, RecordKey> FindCandidatesCarryingObjectsThatStay(IReadOnlySet<int> goingBack)
    {
        var holders = inputs.HoldersOfObjectsThatStay.Find(holder => goingBack.Contains(world.StandingNow[holder.Index]), world.CopiesNowOf(goingBack).ToHashSet());
        return holders.ToDictionary(entry => world.StandingNow[entry.Key.Index], entry => entry.Value.Key);
    }
}
