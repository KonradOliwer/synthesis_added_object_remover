using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The plans of the other kinds a placed record going back is checked against.</summary>
/// <param name="NavmeshesThatStayChanged">The navmeshes that will not match the world without the cleaned mod, in key order.</param>
/// <param name="NavmeshesGoingBack">The members of the navmesh groups going back.</param>
internal sealed record OtherKindsAsPlanned(ImmutableArray<RecordKey> NavmeshesThatStayChanged, IReadOnlySet<RecordKey> NavmeshesGoingBack);

/// <summary>What one pass of the placed guards found.</summary>
/// <param name="Found">By candidate index; null for one that passes or no longer goes back.</param>
/// <param name="ClonesLost">The candidates going back with a copy whose copy fails its rules, by candidate index.</param>
internal sealed record PlacedChecks(PlacedRefusal?[] Found, IReadOnlyDictionary<int, CloneLoss> ClonesLost)
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
/// <param name="TooClose">The too-close rule over the given world, the after-removal world or one with more causes, in which the given objects cause no removals.</param>
/// <param name="Clones">The rules the copies of the candidates going back with one are judged by.</param>
internal sealed record PlacedGuardInputs(
    Func<CollectedObjects, IEnumerable<OtherId>, IPlacementRule> TooClose,
    ObjectsThatBlockActors Blocking,
    INavmeshes Navmeshes,
    CloneGuards Clones);

/// <summary>
/// The guards of the candidates, the placed records whose areas let them go back, each checked as it goes back, with only its undone
/// parts taken from its earlier version. The too-close guard checks a candidate against the causes of <see cref="AfterRemovalWorld"/>,
/// a refused candidate among them where it stands now, less the candidates going back, which move away, every cause that ends
/// removed in the final world (<see cref="FinalPresence"/>) and every one the follow-up of the pass removes, restores or pulls down; a
/// cause it pulls down counts where it lands, and a candidate too close to one there is refused itself, while the candidates that
/// pulled it down go back. A candidate going back with a clone leaves the clone where it stands now:
/// that entry stays among the causes, keeps carrying what rests on it and holds up what goes back onto it. A candidate the follow-up
/// pulls down where it goes back must pass the guards of a place where it lands too, and every target object the follow-up restores
/// where it goes back, or the candidates that started it stay.
/// Thread-safe.
/// </summary>
internal sealed class PlacedGuards(AfterRemovalWorld world, PlacedGuardInputs inputs, DoorLinkGuard doors, Execution execution)
{
    /// <summary>The door rule's consistency checks, made once, one after another.</summary>
    private readonly ComputedOnce<IReadOnlyDictionary<RecordKey, NavmeshFault?>> _doorNavmeshFaults = new(doors.CheckEachNavmeshWithItsDoors);

    /// <summary>
    /// The first guard each candidate going back fails, and the copies that fail their rules, with every other one of them going back
    /// too and the follow-up of the pass written. A copy whose object the follow-up takes along is lost before one too close to a cause,
    /// and that one before one actors walk on.
    /// </summary>
    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="withClone">Those of them going back with a clone.</param>
    /// <param name="pass">Whether a placed record ends removed with <paramref name="goingBack"/> going back.</param>
    /// <param name="followUp">The follow-up of the pass.</param>
    /// <param name="standing">What the too-close check of the cleaned mod's objects that stand found in the pass.</param>
    public PlacedChecks CheckGoingBack(
        IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, OtherKindsAsPlanned plans, PresenceInPass pass, FollowUpChecks followUp, StandingTooCloseFound standing)
    {
        var leaving = goingBack.Where(candidate => !withClone.Contains(candidate)).ToHashSet();
        var landed = followUp.PulledDown();
        var causes = LandedCauses.AddedAfter(world.World, LandedCausesOf(landed));
        HashSet<RecordKey> takenFromTheirPlace = [.. followUp.OthersRemoved(), .. landed.Select(pulled => pulled.Landed.Key), .. followUp.TargetsDecided];
        var tooClose = new TooCloseCheck(inputs.TooClose(causes, CausesLeftOut(leaving, pass, takenFromTheirPlace).OrderBy(cause => cause.Index)), causes);
        var doorNavmeshFaults = _doorNavmeshFaults.Value;
        var place = new PlaceChecks(tooClose, plans, plans.NavmeshesThatStayChanged.ToHashSet());
        var landings = LandingsOf(followUp, goingBack);
        var followUpPlace = landings.Count == 0 && followUp.TargetsRestored.Count == 0
            ? place
            : place with { TooClose = TooCloseAnywhere(LeftOutKeys(leaving, pass, takenFromTheirPlace), LandedCausesOf(landed)) };
        var together = new FoundTogether(
            standing.Refused,
            followUp.HoldingUp,
            followUp.ArrivingWhereTheyDoNotFit,
            LandingsThatDoNotFit(landings, followUpPlace),
            TakingAlongWhatWouldNotFit(followUp, followUpPlace));
        var found = ParallelMap.Run(
            execution,
            world.Restored.Length,
            candidate => goingBack.Contains(candidate) ? FirstRefusal(candidate, place, goingBack, together, doorNavmeshFaults) : null,
            ParallelMap.AutomaticRangeSize);
        var lost = followUp.CopiesLost.ToDictionary(candidate => candidate, _ => new CloneLoss(CloneRefusal.TakenAwayByFollowUp, Failure: null));
        foreach (var (candidate, loss) in standing.CopiesLost) lost.TryAdd(candidate, loss);
        foreach (var (candidate, loss) in inputs.Clones.FindLost(withClone, place.StaysChanged)) lost.TryAdd(candidate, loss);
        return new PlacedChecks(found, lost);
    }

    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="found">What the guards that judge every candidate together found.</param>
    /// <param name="doorNavmeshFaults">What the door rule's consistency checks found, by navmesh.</param>
    private PlacedRefusal? FirstRefusal(
        int candidate, PlaceChecks place, IReadOnlySet<int> goingBack, FoundTogether found, IReadOnlyDictionary<RecordKey, NavmeshFault?> doorNavmeshFaults)
    {
        var restored = world.Restored[candidate];
        return CheckTooClose(restored, place.TooClose)
            ?? found.TooCloseForStanding.GetValueOrDefault(candidate)
            ?? CheckNavmeshes(restored, place.Plans.NavmeshesThatStayChanged, place.StaysChanged)
            ?? RefusalFound(PlacedGuard.HoldsUpOtherModsObject, found.HoldingUp, candidate)
            ?? (found.ArrivingWhereTheyDoNotFit.TryGetValue(candidate, out var misfit) ? ArrivalRefusal(misfit) : null)
            ?? found.LandingsThatDoNotFit.GetValueOrDefault(candidate)
            ?? RefusalFound(PlacedGuard.TakesAlongObjectThatWouldNotFit, found.TakingAlong, candidate)
            ?? doors.Check(candidate, goingBack, place.Plans.NavmeshesGoingBack, doorNavmeshFaults);
    }

    /// <param name="TooCloseForStanding">The candidates whose going back makes one of the cleaned mod's standing objects too close, each with the first refusal.</param>
    /// <param name="HoldingUp">With KeepSupportingObject: the candidates whose leaving leaves an object of a plugin trusted in full without support, each with the first one.</param>
    /// <param name="ArrivingWhereTheyDoNotFit">The candidates that would not fit where they go back and may not be pulled down or removed there, each with why.</param>
    /// <param name="LandingsThatDoNotFit">The candidates the follow-up pulls down where they go back onto a place that fails a guard of a place.</param>
    /// <param name="TakingAlong">The candidates whose going back makes the follow-up restore an object that would not fit, each with the first one.</param>
    private sealed record FoundTogether(
        IReadOnlyDictionary<int, PlacedRefusal> TooCloseForStanding,
        IReadOnlyDictionary<int, RecordKey> HoldingUp,
        IReadOnlyDictionary<int, ArrivalRefused> ArrivingWhereTheyDoNotFit,
        IReadOnlyDictionary<int, PlacedRefusal> LandingsThatDoNotFit,
        IReadOnlyDictionary<int, RecordKey> TakingAlong);

    /// <summary>The checks of a place an object ends at, in one pass.</summary>
    /// <param name="StaysChanged">The navmeshes that stay changed.</param>
    private sealed record PlaceChecks(TooCloseCheck TooClose, OtherKindsAsPlanned Plans, IReadOnlySet<RecordKey> StaysChanged);

    /// <summary>The first guard of the place itself that <paramref name="placed"/> fails there: too close, navmesh.</summary>
    private PlacedRefusal? CheckPlace(TargetObject placed, PlaceChecks place) =>
        CheckTooClose(placed, place.TooClose)
            ?? CheckNavmeshes(placed, place.Plans.NavmeshesThatStayChanged, place.StaysChanged);

    /// <summary>A candidate the follow-up pulls down where it goes back ends where it lands, so that place must pass the guards of a place too.</summary>
    /// <param name="landings">The candidates the follow-up pulls down where they go back, each as it goes back, where it lands.</param>
    private Dictionary<int, PlacedRefusal> LandingsThatDoNotFit(Dictionary<int, TargetObject> landings, PlaceChecks place)
    {
        var refused = new Dictionary<int, PlacedRefusal>();
        foreach (var (candidate, landing) in landings.OrderBy(entry => entry.Key))
        {
            if (CheckPlace(landing, place) is { } refusal) refused.Add(candidate, refusal with { Guard = PlacedGuard.WouldNotFitWherePulledDown });
        }
        return refused;
    }

    /// <summary>
    /// The too-close check of places beyond the candidates' reach, such as where the follow-up pulls a candidate down to or restores a
    /// target object to: among every cause of the after-removal world but those left out, then the causes that land.
    /// </summary>
    /// <param name="leftOut">The keys of the causes left out.</param>
    private TooCloseCheck TooCloseAnywhere(HashSet<RecordKey> leftOut, List<OtherObject> landedCauses)
    {
        List<OtherObject> causes = [.. world.Causes.Where(cause => !leftOut.Contains(cause.Key)).Select((cause, index) => cause with { Id = new OtherId(index) })];
        var checkedWorld = LandedCauses.AddedAfter(world.World with { OtherModObjects = [.. causes] }, landedCauses);
        return new TooCloseCheck(inputs.TooClose(checkedWorld, []), checkedWorld);
    }

    /// <summary>The keys of the causes <see cref="CausesLeftOut"/> leaves out, among every cause.</summary>
    private HashSet<RecordKey> LeftOutKeys(IReadOnlySet<int> leaving, PresenceInPass pass, IReadOnlySet<RecordKey> takenFromTheirPlace) =>
    [
        .. leaving.Select(candidate => world.Restored[candidate].Key),
        .. world.Causes.Where(cause => pass.EndsRemoved(cause.Key) || takenFromTheirPlace.Contains(cause.Key)).Select(cause => cause.Key),
    ];

    /// <summary>The candidates going back that the follow-up pulls down where they go back, each as it goes back, where it lands.</summary>
    private Dictionary<int, TargetObject> LandingsOf(FollowUpChecks followUp, IReadOnlySet<int> goingBack)
    {
        var landingOf = followUp.PulledDown().ToDictionary(pulled => pulled.Landed.Key, pulled => pulled.Landed.Position);
        return goingBack.Where(candidate => landingOf.ContainsKey(world.Restored[candidate].Key))
            .ToDictionary(candidate => candidate, candidate => LandedAt(world.Restored[candidate], landingOf[world.Restored[candidate].Key]));
    }

    private static TargetObject LandedAt(TargetObject restored, Vector3 position) => restored.PlacedAt(restored.Placement with { Position = position });

    /// <summary>
    /// The candidates whose going back makes the follow-up restore a target object that would lose its support where it goes back, then
    /// those whose restored target object fails a guard of its place there, each with the first such object.
    /// </summary>
    private Dictionary<int, RecordKey> TakingAlongWhatWouldNotFit(FollowUpChecks followUp, PlaceChecks place)
    {
        var takingAlong = new Dictionary<int, RecordKey>(followUp.TakingAlongWithoutSupport);
        if (followUp.TargetsRestored.Count == 0) return takingAlong;
        var candidateOf = world.Restored.Select((candidate, index) => KeyValuePair.Create(candidate.Key, index)).ToDictionary();
        foreach (var restored in followUp.TargetsRestored.Where(restored => CheckPlace(restored.Restored, place) is not null))
        {
            foreach (var key in restored.StartedBy) takingAlong.TryAdd(candidateOf[key], restored.Restored.Key);
        }
        return takingAlong;
    }

    /// <summary>
    /// The causes left out: the copies of the candidates that leave where they stand now, those that end removed and those the follow-up
    /// removes or pulls down, where they stood.
    /// </summary>
    /// <param name="leaving">The candidates going back without a clone.</param>
    /// <param name="takenFromTheirPlace">The keys of the objects the follow-up removes or pulls down.</param>
    private HashSet<OtherId> CausesLeftOut(IReadOnlySet<int> leaving, PresenceInPass pass, IReadOnlySet<RecordKey> takenFromTheirPlace) =>
    [
        .. world.CauseCopiesNowOf(leaving),
        .. world.World.OtherModObjects.Where(cause => pass.EndsRemoved(cause.Key) || takenFromTheirPlace.Contains(cause.Key)).Select(cause => cause.Id),
    ];

    /// <summary>
    /// The causes the follow-up pulls down, where they land. An object only support for the removal steps stays no cause when pulled.
    /// A candidate pulled down where it arrives is no cause where it lands either: its own landing is checked instead, and it would be
    /// too close to itself.
    /// </summary>
    private List<OtherObject> LandedCausesOf(List<PulledByFollowUp> pulled)
    {
        var causeKeys = world.Causes.Select(cause => cause.Key).ToHashSet();
        causeKeys.ExceptWith(world.Restored.Select(candidate => candidate.Key));
        return [.. pulled.Select(entry => entry.Landed).Where(other => causeKeys.Contains(other.Key))];
    }

    /// <summary>The too-close rule of a pass and the world of its causes.</summary>
    private sealed record TooCloseCheck(IPlacementRule Rule, CollectedObjects Causes);

    private static PlacedRefusal? CheckTooClose(TargetObject restored, TooCloseCheck tooClose)
    {
        var check = tooClose.Rule.CheckWhereItStands(restored);
        if (check.Failure is { } failure) return TooCloseRefusals.Stopped(failure);
        return TooCloseRefusals.ClashOf(check) is { } clash ? TooCloseRefusals.ByWhatStays(tooClose.Causes.Other(clash.Other).Key) : null;
    }

    /// <summary>The refusal of a candidate that does not fit where it arrives, naming its main cause and every other.</summary>
    private static PlacedRefusal ArrivalRefusal(ArrivalRefused misfit) =>
        new(GuardOf(misfit.Misfit), misfit.Causes.IsEmpty ? null : misfit.Causes[0], Failure: null) { AlsoFound = misfit.Causes.IsEmpty ? [] : misfit.Causes[1..] };

    private static PlacedGuard GuardOf(ArrivalMisfit misfit) => misfit switch
    {
        ArrivalMisfit.LosesSupport => PlacedGuard.ArrivesWithoutSupport,
        ArrivalMisfit.InGround => PlacedGuard.ArrivesInGround,
        ArrivalMisfit.AtWaterLine => PlacedGuard.ArrivesAtWaterLine,
        _ => throw new UnreachableException($"Unknown arrival misfit {misfit}."),
    };

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
}
