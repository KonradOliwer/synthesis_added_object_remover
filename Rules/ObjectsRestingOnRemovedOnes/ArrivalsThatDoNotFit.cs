using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The objects that do not fit where they arrive, measured between the world in which every object that could leave did, on the ground
/// without the cleaned mod's changes, and the world after, in which some stay, the follow-up took its objects away and the ground and
/// water are as planned: one lies partly between the levels of a water line that stays moved, ground rises into it
/// (<see cref="GroundChangeUnder.RisesInto"/>), or it loses its support (<see cref="SupportLoss"/>), the first of these that holds. Only
/// an object of a plugin trusted in full (<see cref="OtherModsObjectRules.IsJudgedWhenObjectsLeave"/>) that loses its support is pulled
/// down or removed from where it arrives, by the fate <see cref="LosingSupportFates"/> gives it; any other one, and one that fate would
/// leave where it arrives, is only found, since it may not be changed there, and so it stays where it stands now. Each arrival found
/// changes the world the others are judged in: one pulled down stands where it lands, one removed is gone, one found stays where it
/// stands now. So the arrivals are judged again until none more fails, and the lowest of those failing is settled first, so a stack
/// settles from the bottom up and none is left floating. Thread-safe.
/// </summary>
/// <param name="everyoneLeft">The world in which every object that could leave stands where it arrives, on the ground without the cleaned mod's changes.</param>
/// <param name="after">The world after the follow-up, with the objects of any plugin unchanged, on the planned ground.</param>
/// <param name="afterChanges">The objects of any plugin moved (where they arrive or land) or taken away (null) in the world after.</param>
/// <param name="arrivingInAnotherShape">
/// The objects arriving in another shape, in id order, which stand in the world after only where they arrive, as support when they can
/// hold up others.
/// </param>
/// <param name="waterLines">The water lines the cleaned mod moved that stay moved.</param>
internal sealed class ArrivalsThatDoNotFit(
    SupportWorld everyoneLeft,
    SupportWorld after,
    IReadOnlyDictionary<OtherId, PlacedTransform?> afterChanges,
    IReadOnlyList<OtherObject> arrivingInAnotherShape,
    ImmutableArray<MovedWaterLine> waterLines,
    OtherModsObjectRules rules,
    OtherObjectSupport support,
    SupportLoss loss,
    GroundChangeUnder ground,
    DropFinder drops,
    OtherModsObjectsSettings settings)
{
    private readonly LosingSupportFates _fates = new(rules, drops, settings);

    /// <summary>An object arriving: where it arrives, and its object when it belongs to any plugin.</summary>
    /// <param name="Other">At the position it arrives at, the only part of its placement a pull-down writes; null for a target object.</param>
    public readonly record struct Arrival(StandingObject Standing, OtherObject? Other);

    /// <summary>What the judging found so far.</summary>
    /// <param name="Changed">The world after's changes, with the arrivals settled: where they land, or no longer where they arrive (null).</param>
    /// <param name="InAnotherShape">The objects arriving in another shape that are not taken away, by id: where they arrive or land.</param>
    /// <param name="TargetsOut">The target objects found, which no longer stand where they arrive.</param>
    /// <param name="Settled">The arrivals settled, by their index: found, or still resting where they arrive (null).</param>
    private sealed record Progress(
        Dictionary<OtherId, PlacedTransform?> Changed, Dictionary<OtherId, OtherObject> InAnotherShape, HashSet<TargetId> TargetsOut, Dictionary<int, ArrivingWhereItDoesNotFit?> Settled);

    /// <summary>Why an arrival does not fit, with the share of its support it lost when it loses its support.</summary>
    private readonly record struct Failing(int Index, ArrivalMisfit Misfit, float LostShare);

    /// <returns>Each arrival that does not fit, in the order given.</returns>
    /// <remarks>One after another: only the candidates of one pass arrive.</remarks>
    public ImmutableArray<ArrivingWhereItDoesNotFit> Find(IReadOnlyList<Arrival> arrivals)
    {
        var queries = new OtherObjectQueries();
        List<float> weightsBefore = [.. arrivals.Select(arrival => support.FindContacts(arrival.Standing, everyoneLeft, queries).TotalWeight)];
        List<ArrivalMisfit?> placeMisfits = [.. arrivals.Select(arrival => PlaceMisfitOf(arrival.Standing))];
        var progress = new Progress(new Dictionary<OtherId, PlacedTransform?>(afterChanges), arrivingInAnotherShape.ToDictionary(other => other.Id), [], []);
        while (LowestFailing(arrivals, weightsBefore, placeMisfits, progress, queries) is { } failing) Settle(arrivals[failing.Index], failing, progress, queries);
        return [.. progress.Settled.OrderBy(entry => entry.Key).Select(entry => entry.Value).OfType<ArrivingWhereItDoesNotFit>()];
    }

    /// <summary>What the place itself does not fit, whatever stands around it: a water line that stays moved, then ground rising into it.</summary>
    private ArrivalMisfit? PlaceMisfitOf(StandingObject standing)
    {
        var bounds = standing.Box.WorldAabb(0f);
        if (waterLines.Any(line => Crosses(line, standing.Space, bounds))) return ArrivalMisfit.AtWaterLine;
        return ground.RisesInto(standing, everyoneLeft.Terrain, after.Terrain) ? ArrivalMisfit.InGround : null;
    }

    /// <summary>Whether the box reaches into the line's cell seen from above and lies partly between its two levels.</summary>
    private static bool Crosses(MovedWaterLine line, RecordKey space, Box box) =>
        space == line.Space && box.Min.Z < line.Upper && box.Max.Z > line.Lower && (line.Grid is not { } grid || ExteriorGrid.ReachesInto(box, grid));

    /// <returns>The arrival not yet settled with the lowest bottom, then the lowest index, that does not fit; null when none fails.</returns>
    private Failing? LowestFailing(
        IReadOnlyList<Arrival> arrivals, List<float> weightsBefore, List<ArrivalMisfit?> placeMisfits, Progress progress, OtherObjectQueries queries)
    {
        var world = WorldOf(progress);
        return Enumerable.Range(0, arrivals.Count)
            .Where(index => !progress.Settled.ContainsKey(index))
            .OrderBy(index => arrivals[index].Standing.Box.WorldAabb(0f).Min.Z)
            .ThenBy(index => index)
            .Select(index => placeMisfits[index] is { } misfit
                ? new Failing(index, misfit, LostShare: 0f)
                : loss.FindLostShare(arrivals[index].Standing, weightsBefore[index], everyoneLeft, world, queries) is { } lostShare
                    ? new Failing(index, ArrivalMisfit.LosesSupport, lostShare)
                    : (Failing?)null)
            .FirstOrDefault(failing => failing is not null);
    }

    private SupportWorld WorldOf(Progress progress) =>
        after.With(progress.Changed).Without(progress.TargetsOut).WithAdded([.. progress.InAnotherShape.Values.Where(rules.CanHoldUp).OrderBy(other => other.Id.Index)]);

    private void Settle(Arrival arrival, Failing failing, Progress progress, OtherObjectQueries queries)
    {
        if (failing.Misfit != ArrivalMisfit.LosesSupport
            || arrival.Other is not { } other
            || !rules.IsJudgedWhenObjectsLeave(other)
            || settings.Handling == OtherModsObjectsHandling.KeepSupportingObject)
        {
            SettleWhereItStandsNow(arrival, failing, progress);
            return;
        }
        var outcome = _fates.FateBeforePulling(other, failing.LostShare)
            ?? _fates.FateOfPull(other, failing.LostShare, drops.FindDrop(arrival.Standing, WorldOf(progress), queries));
        if (outcome.Fate == OtherModsObjectFate.LeftStillTouching)
        {
            progress.Settled.Add(failing.Index, null);
        }
        else if (outcome.Fate == OtherModsObjectFate.PulledDown || outcome.IsRemoved)
        {
            PlaceAt(other, outcome.IsRemoved ? null : arrival.Standing.Transform with { Position = outcome.NewPosition }, progress);
            progress.Settled.Add(failing.Index, new ArrivingWhereItDoesNotFit(arrival.Standing.Self, failing.Misfit, outcome));
        }
        else
        {
            SettleWhereItStandsNow(arrival, failing, progress);
        }
    }

    /// <param name="at">Where it lands; null when it is removed.</param>
    private static void PlaceAt(OtherObject other, PlacedTransform? at, Progress progress)
    {
        if (!progress.InAnotherShape.ContainsKey(other.Id)) progress.Changed[other.Id] = at;
        else if (at is { } landing) progress.InAnotherShape[other.Id] = other with { Position = landing.Position };
        else progress.InAnotherShape.Remove(other.Id);
    }

    /// <summary>The arrival may not be changed where it arrives, so it does not arrive: it stays where and as it stands now.</summary>
    private static void SettleWhereItStandsNow(Arrival arrival, Failing failing, Progress progress)
    {
        if (arrival.Other is { } other)
        {
            progress.InAnotherShape.Remove(other.Id);
            progress.Changed.Remove(other.Id);
        }
        else
        {
            progress.TargetsOut.Add(new TargetId(arrival.Standing.Self.Index));
        }
        progress.Settled.Add(failing.Index, new ArrivingWhereItDoesNotFit(arrival.Standing.Self, failing.Misfit, Outcome: null));
    }
}
