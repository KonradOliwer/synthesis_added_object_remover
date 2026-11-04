using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
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
/// <param name="ClonesLost">The candidates going back with a copy whose copy fails its rules, by candidate index.</param>
/// <param name="CausesLeftOut">The causes the too-close rules left out for every candidate in the pass, which the next pass compares with its own.</param>
internal sealed record PlacedChecks(PlacedRefusal?[] Found, IReadOnlyDictionary<int, CloneLoss> ClonesLost, IReadOnlySet<OtherId> CausesLeftOut)
{
    /// <summary>The state's refusals and copy outcomes with what the pass found; a candidate a guard refuses keeps its copy's outcome as it was.</summary>
    public (ImmutableArray<PlacedRefusal?> Placed, ImmutableArray<CloneJudged?> Clones) AppliedTo(RestoreState state)
    {
        ImmutableArray<PlacedRefusal?> placed = [.. state.Placed.Select((refusal, index) => refusal ?? Found[index])];
        ImmutableArray<CloneJudged?> clones =
            [.. state.Clones.Select((clone, index) => placed[index] is null && ClonesLost.TryGetValue(index, out var lost) ? clone! with { RefusedBy = lost.By, Failure = lost.Failure } : clone)];
        return (placed, clones);
    }
}

/// <summary>What the placed guards read besides the after-removal world.</summary>
/// <param name="Caches">The object caches over the after-removal world.</param>
/// <param name="TooClose">The too-close rule over the after-removal world in which the given objects cause no removals.</param>
/// <param name="TooCloseByGroup">The too-close check of the targets that stay with their own groups' settings.</param>
/// <param name="HoldersOfObjectsThatStay">The support test over the after-removal world, among the candidates that stand now.</param>
/// <param name="HoldersOfWhatGoesBack">
/// The support test over <see cref="AfterRemovalWorld.GoingBackWorld"/>, among <see cref="AfterRemovalWorld.Holders"/>.
/// </param>
/// <param name="FindLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
/// <param name="FindTerrainWinner">The plugin of the winning LAND record of a cell; null where the cell has none.</param>
/// <param name="Clones">The rules the copies of the candidates going back with one are judged by.</param>
internal sealed record PlacedGuardInputs(
    IObjectCaches Caches,
    Func<IEnumerable<OtherId>, IPlacementRule> TooClose,
    GroupTooCloseGuard TooCloseByGroup,
    IHoldersSearch HoldersOfObjectsThatStay,
    IHoldersSearch HoldersOfWhatGoesBack,
    IBaseObjectShapes Shapes,
    ObjectsThatBlockActors Blocking,
    INavmeshes Navmeshes,
    Func<RecordKey, RecordKey?> FindLandWorldspace,
    Func<ExteriorCell, PluginName?> FindTerrainWinner,
    CloneGuards Clones);

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
/// A candidate going back with a clone leaves the clone where it stands now: that entry stays among the causes, keeps carrying what
/// rests on it and holds up what goes back onto it.
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

    /// <summary>The objects that stand as they stood before the cleaned mod, the same in every pass.</summary>
    private readonly ComputedOnce<HashSet<OtherId>> _standingAsBefore = new(() => world.StandingAsBeforeTheCleanedMod());

    /// <summary>The first guard each candidate going back fails, and the copies that fail their rules, with every other one of them going back too.</summary>
    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="withClone">Those of them going back with a clone.</param>
    /// <param name="causesLeftOutBefore">
    /// What the pass before left out of the causes; null in the first pass. The too-close rule checks again only the candidates
    /// that a cause no longer left out can reach: a candidate it passed before passes again otherwise.
    /// </param>
    public PlacedChecks CheckGoingBack(IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, OtherKindsAsPlanned plans, IReadOnlySet<OtherId>? causesLeftOutBefore)
    {
        var leaving = goingBack.Where(candidate => !withClone.Contains(candidate)).ToHashSet();
        var causesLeftOut = CausesLeftOut(goingBack, leaving);
        var rules = CreateTooCloseRules(causesLeftOut, goingBack);
        // Covers the candidates that end in their earlier shape and place too: the causes restored by removal steps they also leave out are the same in every pass.
        var checkTooClose = causesLeftOutBefore is null ? null : CandidatesReaching([.. causesLeftOutBefore.Except(causesLeftOut).OrderBy(cause => cause.Index)], goingBack);
        var holdingInvisible = _holdingInvisible.Value;
        var tooCloseByGroup = inputs.TooCloseByGroup.Find(candidate => EndsWhereItGoesBack(candidate, goingBack));
        var carrying = FindCandidatesCarryingObjectsThatStay(leaving);
        var resting = FindCandidatesRestingOnWhatDoesNotStay(goingBack, leaving);
        var doorNavmeshFaults = _doorNavmeshFaults.Value;
        var staysChanged = plans.NavmeshesThatStayChanged.ToHashSet();
        var found = ParallelMap.Run(
            execution,
            world.Restored.Length,
            candidate => goingBack.Contains(candidate)
                ? FirstRefusal(
                    candidate,
                    checkTooClose?.Contains(candidate) == false ? null : rules.For(candidate),
                    goingBack,
                    plans,
                    staysChanged,
                    new(tooCloseByGroup, holdingInvisible, carrying, resting),
                    doorNavmeshFaults)
                : null,
            ParallelMap.AutomaticRangeSize);
        return new PlacedChecks(found, inputs.Clones.FindLost(goingBack, withClone, staysChanged), causesLeftOut);
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
    /// <param name="found">What the guards that judge every candidate together found.</param>
    /// <param name="doorNavmeshFaults">What the door rule's consistency checks found, by navmesh.</param>
    private PlacedRefusal? FirstRefusal(
        int candidate,
        IPlacementRule? rule,
        IReadOnlySet<int> goingBack,
        OtherKindsAsPlanned plans,
        IReadOnlySet<RecordKey> staysChanged,
        FoundTogether found,
        IReadOnlyDictionary<RecordKey, NavmeshFault?> doorNavmeshFaults)
    {
        var restored = world.Restored[candidate];
        return (rule is null ? null : CheckTooClose(restored, rule))
            ?? found.TooCloseByGroup.GetValueOrDefault(candidate)
            ?? CheckGround(candidate, plans.Terrain)
            ?? CheckWater(restored, plans.Water)
            ?? RefusalFound(PlacedGuard.HoldsInvisibleObjectThatStays, found.HoldingInvisible, candidate)
            ?? CheckNavmeshes(restored, plans.NavmeshesThatStayChanged, staysChanged)
            ?? RefusalFound(PlacedGuard.CarriesObjectThatStays, found.Carrying, candidate)
            ?? RefusalFound(PlacedGuard.RestsOnWhatDoesNotStay, found.Resting, candidate)
            ?? doors.Check(candidate, goingBack, plans.NavmeshesGoingBack, doorNavmeshFaults);
    }

    /// <param name="TooCloseByGroup">The candidates a target that stays comes too close to by its own group's settings, each with the first refusal.</param>
    /// <param name="HoldingInvisible">The candidates holding an invisible object of the cleaned mod, each with the first one.</param>
    /// <param name="Carrying">The candidates an object that stays rests on, each with the first one.</param>
    /// <param name="Resting">The candidates that rest where they go back on an object that does not end there, each with the first one.</param>
    private sealed record FoundTogether(
        IReadOnlyDictionary<int, PlacedRefusal> TooCloseByGroup,
        IReadOnlyDictionary<int, RecordKey> HoldingInvisible,
        IReadOnlyDictionary<int, RecordKey> Carrying,
        IReadOnlyDictionary<int, RecordKey> Resting);

    /// <summary>The causes left out for every candidate: the copies of the candidates that leave where they stand now, and those that end removed.</summary>
    /// <param name="leaving">The candidates going back without a clone.</param>
    private HashSet<OtherId> CausesLeftOut(IReadOnlySet<int> goingBack, IReadOnlySet<int> leaving) =>
    [
        .. world.CauseCopiesNowOf(leaving),
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
    /// Ground that stays changed through the cleaned mod lies under the box's footprint (the box around the object, seen from above),
    /// within the box's height range: ground the terrain plan keeps as the cleaned mod made it, each point at the height the patch
    /// leaves there, blended or not. Ground only an unrelated plugin changed does not count: the world without the cleaned mod has it
    /// too. The winning LAND record's plugin is named as the one that keeps the ground. A candidate that keeps its shape where
    /// it stands now, such as one that keeps a later plugin's position and whose undone parts shape nothing, is not checked: its
    /// restore changes nothing between it and the ground.
    /// </summary>
    private PlacedRefusal? CheckGround(int candidate, TerrainUndoPlan terrain)
    {
        var restored = world.Restored[candidate];
        if (world.KeepShapeWhereItStandsNow[candidate] || inputs.FindLandWorldspace(restored.SpaceKey) is not { } land) return null;
        var box = BoxOf(restored);
        var footprint = (new Vector2(box.Min.X, box.Min.Y), new Vector2(box.Max.X, box.Max.Y));
        if (terrain.GroundThatStaysChangedUnder(land, footprint, (box.Min.Z, box.Max.Z)) is not { } changed) return null;
        var keptBy = inputs.FindTerrainWinner(changed.Cell)
            ?? throw new UnreachableException($"The ground of cell {changed.Cell} stays changed, but no LAND record of it wins.");
        return new PlacedRefusal(PlacedGuard.OnGroundThatStaysChanged, changed.Land, Failure: null, keptBy, changed.Gap);
    }

    /// <summary>The box lies partly between a cell's water level before the cleaned mod and the one that stays.</summary>
    private PlacedRefusal? CheckWater(TargetObject restored, WaterUndoPlan water) =>
        water.CellWithChangedWaterLineIn(restored.SpaceKey, BoxOf(restored)) is { } cell ? new PlacedRefusal(PlacedGuard.AtWaterLineThatStaysChanged, cell, Failure: null) : null;

    private Box BoxOf(TargetObject placed) => BaseBoxes.Of(inputs.Shapes, placed.Base, placed.Transform).WorldAabb(0f);

    private PlacedRefusal? CheckTooClose(TargetObject restored, IPlacementRule rule)
    {
        var check = rule.CheckWhereItStands(restored);
        if (check.Failure is { } failure) return TooCloseRefusals.Stopped(failure);
        return TooCloseRefusals.ClashOf(check) is { } clash ? TooCloseRefusals.ByWhatStays(world.World.Other(clash.Other).Key) : null;
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

    /// <summary>
    /// The candidates leaving where they stand now that an object that stays rests on there, each with the first such object. For a
    /// candidate that ends in its earlier shape and place, an object that stands as it stood before the cleaned mod does not count:
    /// both end as they stood then.
    /// </summary>
    /// <param name="leaving">The candidates going back without a clone.</param>
    private Dictionary<int, RecordKey> FindCandidatesCarryingObjectsThatStay(IReadOnlySet<int> leaving)
    {
        var copiesOfMoving = world.CopiesNowOf(leaving).ToHashSet();
        return FindCarrying(leaving, copiesOfMoving, inEarlierShapeAndPlace: true, _standingAsBefore.Value.Contains)
            .Concat(FindCarrying(leaving, copiesOfMoving, inEarlierShapeAndPlace: false, _ => false))
            .ToDictionary();
    }

    /// <summary>The candidates leaving that do or do not end in their earlier shape and place and that an object that stays and is asked about rests on.</summary>
    /// <param name="copiesOfMoving">The copies of every candidate leaving, which neither rest nor hold up.</param>
    private IEnumerable<KeyValuePair<int, RecordKey>> FindCarrying(
        IReadOnlySet<int> leaving, IReadOnlySet<OtherId> copiesOfMoving, bool inEarlierShapeAndPlace, Func<OtherId, bool> notAskedAbout)
    {
        bool MovesAway(TargetId holder)
        {
            var candidate = world.StandingNow[holder.Index];
            return leaving.Contains(candidate) && world.EndInEarlierShapeAndPlace[candidate] == inEarlierShapeAndPlace;
        }

        return inputs.HoldersOfObjectsThatStay.Find(MovesAway, copiesOfMoving, notAskedAbout)
            .Select(entry => KeyValuePair.Create(world.StandingNow[entry.Key.Index], entry.Value.Key));
    }

    /// <summary>
    /// The candidates going back that rest where they go back on an object that does not end there, by the support test of the other
    /// mods' objects, each with the first such holder in holder order: a candidate refused or ending removed, which stays where it
    /// stands now or nowhere, or an object the patch removed or pulled down. Of the holders the test names, each with the first
    /// candidate it holds up, so one holding up several refuses one of them per pass, and the passes refuse the rest.
    /// </summary>
    /// <remarks>The copies of the candidates leaving where they stand now are gone from that place.</remarks>
    /// <param name="leaving">The candidates going back without a clone.</param>
    private Dictionary<int, RecordKey> FindCandidatesRestingOnWhatDoesNotStay(IReadOnlySet<int> goingBack, IReadOnlySet<int> leaving)
    {
        var candidates = world.Restored.Length;
        var candidateOfCopy = Enumerable.Range(0, candidates).ToDictionary(world.RestoredCopyOf);
        HashSet<OtherId> copiesGone =
            [.. world.CopiesNowOf(leaving), .. Enumerable.Range(0, candidates).Where(candidate => !EndsWhereItGoesBack(candidate, goingBack)).Select(world.RestoredCopyOf)];
        var held = inputs.HoldersOfWhatGoesBack.Find(
            holder => holder.Index >= candidates || !EndsWhereItGoesBack(holder.Index, goingBack),
            copiesGone,
            id => !candidateOfCopy.TryGetValue(id, out var candidate) || !EndsWhereItGoesBack(candidate, goingBack));
        var first = new Dictionary<int, RecordKey>();
        foreach (var (holder, other) in held.OrderBy(entry => entry.Key.Index)) first.TryAdd(candidateOfCopy[other.Id], world.Holders[holder.Index].Key);
        return first;
    }

    /// <summary>The candidate goes back and is present in the final world there.</summary>
    private bool EndsWhereItGoesBack(int candidate, IReadOnlySet<int> goingBack) =>
        goingBack.Contains(candidate) && !presence.EndsRemoved(world.Restored[candidate].Key, goingBack.Contains);
}
