using System.Collections.Immutable;
using System.Diagnostics;
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
    OtherModsObjectsSettings settings,
    Execution execution)
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

    /// <summary>What the judging measures of each arrival once, before any is settled.</summary>
    /// <param name="WeightsBefore">The weight of its surface touching anything in the world in which every object that could leave did.</param>
    /// <param name="PlaceMisfits">What its place itself does not fit; null when nothing.</param>
    /// <param name="LowestFirst">The arrivals' indexes, lowest bottom first, then by index.</param>
    private sealed record Measured(float[] WeightsBefore, ArrivalMisfit?[] PlaceMisfits, ImmutableArray<int> LowestFirst);

    /// <summary>An arrival's lost share in a world after as some arrivals settled it, with the supporters it read there.</summary>
    /// <remarks>
    /// Every such world stands on the same ground, so in a later one that gives the arrival the same supporters its lost share is the
    /// same (<see cref="SupportLoss.FindSupportersRead"/>): only the arrivals near what a settled one changed are measured again.
    /// </remarks>
    private sealed record LostShareIn(SupportersRead Read, float? LostShare);

    /// <returns>Each arrival that does not fit, in the order given.</returns>
    public ImmutableArray<ArrivingWhereItDoesNotFit> Find(IReadOnlyList<Arrival> arrivals)
    {
        var measured = Measure(arrivals);
        var lostShares = new LostShareIn?[arrivals.Count];
        var progress = new Progress(new Dictionary<OtherId, PlacedTransform?>(afterChanges), arrivingInAnotherShape.ToDictionary(other => other.Id), [], []);
        var queries = new OtherObjectQueries();
        while (true)
        {
            var world = WorldOf(progress);
            if (LowestFailing(arrivals, measured, lostShares, progress, world) is not { } failing) break;
            Settle(arrivals[failing.Index], failing, world, progress, queries);
        }
        return [.. progress.Settled.OrderBy(entry => entry.Key).Select(entry => entry.Value).OfType<ArrivingWhereItDoesNotFit>()];
    }

    private Measured Measure(IReadOnlyList<Arrival> arrivals) =>
        new(
            MeasureEach([.. Enumerable.Range(0, arrivals.Count)], (index, queries) => support.FindContacts(arrivals[index].Standing, everyoneLeft, queries).TotalWeight),
            [.. arrivals.Select(arrival => PlaceMisfitOf(arrival.Standing))],
            [.. Enumerable.Range(0, arrivals.Count).OrderBy(index => arrivals[index].Standing.Box.WorldAabb(0f).Min.Z).ThenBy(index => index)]);

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
    /// <remarks>
    /// Only the arrivals below the lowest whose place does not fit can be the lowest failing, so only their support is measured; when
    /// none of them loses its support, that one is the lowest failing.
    /// </remarks>
    /// <param name="world">The world after as the arrivals settled so far leave it.</param>
    private Failing? LowestFailing(IReadOnlyList<Arrival> arrivals, Measured measured, LostShareIn?[] lostShares, Progress progress, SupportWorld world)
    {
        List<int> unsettled = [.. measured.LowestFirst.Where(index => !progress.Settled.ContainsKey(index))];
        List<int> judged = [.. unsettled.TakeWhile(index => measured.PlaceMisfits[index] is null)];
        var shares = MeasureLostShares(arrivals, measured.WeightsBefore, judged, world, lostShares);
        for (var k = 0; k < judged.Count; k++)
        {
            if (shares[k] is { } share) return new Failing(judged[k], ArrivalMisfit.LosesSupport, share);
        }
        if (judged.Count == unsettled.Count) return null;
        var misfitting = unsettled[judged.Count];
        return new Failing(misfitting, measured.PlaceMisfits[misfitting] ?? throw new UnreachableException($"Arrival {misfitting} stopped the judged ones but fits its place."), LostShare: 0f);
    }

    /// <summary>
    /// Brings the lost share of each arrival of <paramref name="judged"/> up to <paramref name="world"/>, measuring it again only when
    /// the arrival reads other supporters there than where it was measured last.
    /// </summary>
    /// <returns>Share k is that of arrival <paramref name="judged"/>[k] in <paramref name="world"/>.</returns>
    private List<float?> MeasureLostShares(IReadOnlyList<Arrival> arrivals, float[] weightsBefore, List<int> judged, SupportWorld world, LostShareIn?[] lostShares)
    {
        var reads = MeasureEach(judged, (index, queries) => loss.FindSupportersRead(arrivals[index].Standing, world, queries));
        List<(int Index, SupportersRead Read)> changed = [.. judged.Zip(reads).Where(entry => lostShares[entry.First] is not { } known || !known.Read.IsSameAs(entry.Second))];
        var found = MeasureEach(
            [.. changed.Select(entry => entry.Index)], (index, queries) => loss.FindLostShare(arrivals[index].Standing, weightsBefore[index], everyoneLeft, world, queries));
        for (var k = 0; k < changed.Count; k++) lostShares[changed[k].Index] = new LostShareIn(changed[k].Read, found[k]);
        return [.. judged.Select(index => lostShares[index] is { } known ? known.LostShare : throw new UnreachableException($"Arrival {index} was judged but not measured."))];
    }

    /// <returns>Result k is the measure of arrival <paramref name="indexes"/>[k].</returns>
    private T[] MeasureEach<T>(IReadOnlyList<int> indexes, Func<int, OtherObjectQueries, T> measure) =>
        ParallelMap.RunOnEach(execution, indexes, () => new OtherObjectQueries(), measure);

    private SupportWorld WorldOf(Progress progress) =>
        after.With(progress.Changed)
            .Without(progress.TargetsOut)
            .WithAdded([.. progress.InAnotherShape.Values.Where(rules.CanHoldUp).OrderBy(other => other.Id.Index)]);

    /// <param name="world">The world after as the arrivals settled before this one leave it.</param>
    private void Settle(Arrival arrival, Failing failing, SupportWorld world, Progress progress, OtherObjectQueries queries)
    {
        if (failing.Misfit != ArrivalMisfit.LosesSupport
            || arrival.Other is not { } other
            || !rules.IsJudgedWhenObjectsLeave(other)
            || settings.Handling == OtherModsObjectsHandling.KeepSupportingObject)
        {
            SettleWhereItStandsNow(arrival, failing, CauseOf(arrival, failing, world, queries), progress);
            return;
        }
        var outcome = _fates.FateBeforePulling(other, failing.LostShare)
            ?? _fates.FateOfPull(other, failing.LostShare, drops.FindDrop(arrival.Standing, world, queries));
        if (outcome.Fate == OtherModsObjectFate.LeftStillTouching)
        {
            progress.Settled.Add(failing.Index, null);
        }
        else if (outcome.Fate == OtherModsObjectFate.PulledDown || outcome.IsRemoved)
        {
            PlaceAt(other, outcome.IsRemoved ? null : arrival.Standing.Transform with { Position = outcome.NewPosition }, progress);
            progress.Settled.Add(failing.Index, new ArrivingWhereItDoesNotFit(arrival.Standing.Self, failing.Misfit, CauseOf(arrival, failing, world, queries), outcome));
        }
        else
        {
            SettleWhereItStandsNow(arrival, failing, CauseOf(arrival, failing, world, queries), progress);
        }
    }

    /// <param name="world">The world after in which the arrival does not fit.</param>
    private ArrivalCause CauseOf(Arrival arrival, Failing failing, SupportWorld world, OtherObjectQueries queries) => failing.Misfit switch
    {
        ArrivalMisfit.LosesSupport => new ArrivalCause(loss.FindLostSupporters(arrival.Standing, everyoneLeft, world, queries), WaterLineCell: null),
        ArrivalMisfit.InGround => new ArrivalCause([Supporter.Terrain], WaterLineCell: null),
        ArrivalMisfit.AtWaterLine => new ArrivalCause(LostSupports: [], waterLines.First(line => Crosses(line, arrival.Standing.Space, arrival.Standing.Box.WorldAabb(0f))).Cell),
        var unknown => throw new UnreachableException($"Unknown arrival misfit {unknown}."),
    };

    /// <param name="at">Where it lands; null when it is removed.</param>
    private static void PlaceAt(OtherObject other, PlacedTransform? at, Progress progress)
    {
        if (!progress.InAnotherShape.ContainsKey(other.Id)) progress.Changed[other.Id] = at;
        else if (at is { } landing) progress.InAnotherShape[other.Id] = other with { Position = landing.Position };
        else progress.InAnotherShape.Remove(other.Id);
    }

    /// <summary>The arrival may not be changed where it arrives, so it does not arrive: it stays where and as it stands now.</summary>
    private static void SettleWhereItStandsNow(Arrival arrival, Failing failing, ArrivalCause cause, Progress progress)
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
        progress.Settled.Add(failing.Index, new ArrivingWhereItDoesNotFit(arrival.Standing.Self, failing.Misfit, cause, Outcome: null));
    }
}
