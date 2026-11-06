using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the removal steps' follow-up does with the candidates leaving where they stand now, in one pass.</summary>
/// <param name="HoldingUp">
/// With KeepSupportingObject: each candidate whose leaving leaves an object of a plugin trusted in full without support, with the
/// first such object's key in id order.
/// </param>
/// <param name="CopiesLost">The candidates going back with a copy whose object the follow-up takes along.</param>
/// <param name="Written">
/// What the follow-up decides, each with the candidates it started from: written, or only reported for an object it leaves where it
/// stands although it loses its support.
/// </param>
/// <param name="TargetsDecided">The keys of the target objects of <paramref name="Written"/>, which no longer stand where they stand now.</param>
internal sealed record FollowUpChecks(IReadOnlyDictionary<int, RecordKey> HoldingUp, IReadOnlySet<int> CopiesLost, RestoreFollowUp Written, IReadOnlySet<RecordKey> TargetsDecided)
{
    /// <summary>
    /// The candidates that would not fit where they arrive and that the follow-up may not pull down or remove, each with why: candidates
    /// that are targets, objects of plugins not trusted in full or that the handling or their references keep, and every one that
    /// does not fit for another reason than losing its support.
    /// </summary>
    public IReadOnlyDictionary<int, ArrivalMisfit> ArrivingWhereTheyDoNotFit { get; init; } = new Dictionary<int, ArrivalMisfit>();

    /// <summary>
    /// The candidates whose going back makes the follow-up restore a target object that would not fit where it goes back, each with the
    /// first such object in target order.
    /// </summary>
    public IReadOnlyDictionary<int, RecordKey> TakingAlongWithoutSupport { get; init; } = new Dictionary<int, RecordKey>();

    /// <summary>The target objects the follow-up restores, each where it goes back to, in target order.</summary>
    public IReadOnlyList<RestoredByFollowUp> TargetsRestored { get; init; } = [];

    /// <summary>
    /// With KeepSupportingObject: the objects of plugins trusted in full the ground going down would leave without support, directly or
    /// through what goes with it, where they stand now, in id order; the ground under them stays.
    /// </summary>
    public IReadOnlyList<OtherObject> GroundHolds { get; init; } = [];

    public static FollowUpChecks None() => new(new Dictionary<int, RecordKey>(), new HashSet<int>(), RestoreFollowUp.None, new HashSet<RecordKey>());

    /// <summary>The keys of the objects of any plugin the follow-up removes.</summary>
    public HashSet<RecordKey> OthersRemoved() => [.. Written.Others.Where(other => other.Outcome.IsRemoved).Select(other => other.Outcome.Object.Key)];

    /// <summary>The objects of any plugin the follow-up pulls down, each where it lands, with the candidates it started from, in id order.</summary>
    public List<PulledByFollowUp> PulledDown() =>
    [
        .. Written.Others.Where(other => other.Outcome.Fate == OtherModsObjectFate.PulledDown)
            .Select(other => new PulledByFollowUp(
                other.Outcome.Object with { Position = other.Outcome.NewPosition }, other.StartedBy, other.ByGround ? other.Outcome.Object : null)),
    ];
}

/// <param name="Landed">
/// The object where it lands; its id as among the run's objects of any plugin, or after them for a candidate the game shows again.
/// </param>
/// <param name="StartedBy">The keys of the candidates whose going back pulls it down, in key order.</param>
/// <param name="ByGround">The object where it stands now, when ground going down started its pull too; null otherwise.</param>
internal sealed record PulledByFollowUp(OtherObject Landed, ImmutableArray<RecordKey> StartedBy, OtherObject? ByGround);

/// <param name="Restored">The target object as its earlier version, where it goes back to.</param>
/// <param name="StartedBy">The keys of the candidates whose going back restores it, in key order.</param>
internal sealed record RestoredByFollowUp(TargetObject Restored, ImmutableArray<RecordKey> StartedBy);

/// <summary>
/// Asks the removal steps' follow-up (<see cref="IFollowUpOfObjectsLeaving"/>) about the candidates leaving where they stand now: a
/// target candidate leaves as restored to its own earlier version on a working copy of the run's decisions (removed when that version
/// stands nowhere), any other one as an object of any plugin with the parts that go back taken from its earlier version (taken away
/// when it ends removed, and arriving anew when those parts give it another shape). A candidate the game shows again only arrives. A
/// candidate going back with a copy does not leave: the copy stands there, as the object of the run, and is judged like the cleaned
/// mod's own objects, so the copy is lost when the follow-up takes that object along. The follow-up takes along target objects, copies
/// and objects of plugins trusted in full only; the objects of every other plugin stay as they are. What it decides is written, so it
/// keeps no candidate in place, but with KeepSupportingObject, where an object of a plugin
/// trusted in full the leaving would leave without support keeps the candidates it started from where they stand now, as the removal
/// steps keep a supporting object. A candidate that would not fit where it arrives, measured against the world in which the candidates
/// kept in place and every other placed record and every height the cleaned mod changed went back too (only what the cleaned mod,
/// the run or the restore took away counts, not what an unrelated mod changed; the ground and water lines the cleaned mod made that stay are its content), is
/// pulled down or removed there when it loses its support and is an object of a plugin trusted in full and not a target; any other one
/// is kept where it stands now, so it never floats, ends in the ground or at a water line the cleaned mod moved. A target object the
/// follow-up restores that would not fit where it goes back keeps the candidates it started from where they stand now. The ground the
/// pass plans is the ground of the world after: an object of a plugin trusted in full resting on ground that goes down is followed up
/// as one resting on a candidate, and with KeepSupportingObject the ground under it stays; every other object stays as it
/// is, also when ground goes down. Thread-safe.
/// </summary>
/// <param name="final">The decisions with every round and every move.</param>
/// <param name="othersTakenAway">The objects of any plugin the run pulled down (where they stand now) or removed (null), by id.</param>
/// <param name="cleared">The placed records whose areas let them go back, by candidate index.</param>
internal sealed class FollowUpOfGoingBack(
    IFollowUpOfObjectsLeaving rule,
    IRemovalDecisions final,
    IReadOnlyDictionary<OtherId, OtherObject?> othersTakenAway,
    IReadOnlyList<RecordChangedByCleanedMod> cleared,
    CollectedObjects collected)
{
    private readonly ComputedOnce<ChangesOnceBack> _onceBack = new(() => ChangesOnceBack.Of(cleared, collected));

    /// <summary>A candidate that is no target, as its object where it stands now or, for one the game shows again, as it stands once back.</summary>
    private readonly record struct OtherCandidate(OtherObject Object, int Candidate);

    /// <param name="Candidates">The candidates leaving that do not fit where they arrive and stay where they stand now, each with why.</param>
    /// <param name="Targets">The target objects the follow-up restores that do not fit where they arrive.</param>
    private sealed record RefusedArrivals(Dictionary<int, ArrivalMisfit> Candidates, List<ArrivingWhereItDoesNotFit> Targets);

    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="withClone">Those of them going back with a copy.</param>
    /// <param name="pass">Whether a placed record ends removed with <paramref name="goingBack"/> going back.</param>
    /// <param name="land">The ground and water the pass plans; null for the ground and water as they are.</param>
    public FollowUpChecks Check(IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, PresenceInPass pass, LandAfter? land)
    {
        List<int> leaving = [.. goingBack.Where(candidate => !withClone.Contains(candidate)).Order()];
        var targetsLeaving = leaving.Where(candidate => cleared[candidate].Target is not null).ToDictionary(candidate => cleared[candidate].Target!.Value);
        var othersLeaving = StandingOthersOf(leaving.Where(candidate => cleared[candidate].Target is null));
        var shownAgain = ShownAgainOf(leaving, pass);
        var copies = StandingOthersOf(withClone.Where(candidate => cleared[candidate].Target is null));
        var outcomes = rule.Find(
            new WorldStanding(final, othersTakenAway), Decided(targetsLeaving, pass), LeavingOf(goingBack, leaving, othersLeaving, shownAgain, copies, pass, land));
        var othersLeavingById = othersLeaving.Concat(shownAgain).ToDictionary(entry => entry.Object.Id, entry => entry.Candidate);
        ThrowOnLeavingCandidateFollowed(outcomes, targetsLeaving, othersLeavingById);
        var starters = FollowUpStarters.Of(outcomes, targetsLeaving, othersLeavingById);
        var written = Written(outcomes, starters);
        var refused = ArrivalsRefused(outcomes, targetsLeaving, othersLeavingById);
        var heldUp = outcomes.HeldUp.Select(held => (held.Object, Started: starters.StartersOf(held.LostFrom, held.Object.Key))).ToList();
        return new FollowUpChecks(HoldingUpOf(heldUp), CopiesLost(outcomes, withClone, copies), written, TargetsDecidedOf(outcomes))
        {
            TargetsRestored = TargetsRestoredOf(written),
            ArrivingWhereTheyDoNotFit = refused.Candidates,
            TakingAlongWithoutSupport = TakingAlongOf(refused.Targets, starters),
            GroundHolds = [.. heldUp.Where(held => held.Started.ByGround).Select(held => held.Object)],
        };
    }

    /// <summary>
    /// The candidates leaving as objects of any plugin, each where it arrives, the copies, and the candidates that stay, each where it
    /// would arrive had it left too; each of them in the shape it takes once back when that shape differs, with those the game shows again.
    /// </summary>
    /// <param name="leaving">The candidates going back without a copy.</param>
    /// <param name="othersLeaving">The candidates leaving that are no targets and stand now, each as its object where it stands now.</param>
    /// <param name="shownAgain">The candidates leaving that the game shows again, each as it stands once back.</param>
    /// <param name="copies">The candidates going back with a copy that are no targets, each as its object where it stands now.</param>
    private ObjectsLeaving LeavingOf(
        IReadOnlySet<int> goingBack,
        List<int> leaving,
        List<OtherCandidate> othersLeaving,
        List<OtherCandidate> shownAgain,
        List<OtherCandidate> copies,
        PresenceInPass pass,
        LandAfter? land)
    {
        List<int> staying = [.. Enumerable.Range(0, cleared.Count).Where(candidate => !goingBack.Contains(candidate))];
        var onceBack = _onceBack.Value;
        var arriving = ArrivingWhereTheyGoBack(othersLeaving, pass);
        var stayingArriving = ArrivingWhereTheyGoBack(StandingOthersOf(staying.Where(candidate => cleared[candidate].Target is null)), pass);
        return new ObjectsLeaving(
            [.. arriving.Leaving],
            [.. copies.Select(entry => entry.Object)],
            [.. stayingArriving.Leaving.Concat(onceBack.NoCandidateOthers.Leaving).OrderBy(other => other.Object.Id.Index)],
            [
                .. staying.Where(candidate => cleared[candidate].Target is not null)
                    .Select(candidate => new GoingBack(cleared[candidate].Target!.Value, new GoingBackWhereRemoved(), cleared[candidate].Earlier))
                    .Concat(onceBack.NoCandidateTargets),
            ],
            [
                .. stayingArriving.InAnotherShape.Concat(onceBack.NoCandidateOthers.InAnotherShape).Concat(HiddenThatStayAway(leaving))
                    .OrderBy(other => other.Id.Index),
            ],
            [.. arriving.InAnotherShape.Concat(shownAgain.Select(entry => entry.Object)).OrderBy(other => other.Id.Index)],
            land);
    }

    /// <summary>
    /// The placed records of other plugins the game no longer shows that stood before the cleaned mod's change and do not leave: one
    /// going back with a copy does not leave either, so each is either here or among those shown again or ending removed.
    /// </summary>
    /// <param name="leaving">The candidates going back without a copy.</param>
    private IEnumerable<OtherObject> HiddenThatStayAway(List<int> leaving)
    {
        var leavingKeys = leaving.Select(candidate => cleared[candidate].Key).ToHashSet();
        return _onceBack.Value.HiddenByKey.Values.Where(hidden => !leavingKeys.Contains(hidden.Key));
    }

    /// <summary>The candidates leaving that the game shows again and that do not end removed, each as it stands once back, in id order.</summary>
    private List<OtherCandidate> ShownAgainOf(List<int> leaving, PresenceInPass pass)
    {
        var hidden = _onceBack.Value.HiddenByKey;
        return
        [
            .. leaving.Where(candidate => !pass.EndsRemoved(cleared[candidate].Key) && hidden.ContainsKey(cleared[candidate].Key))
                .Select(candidate => new OtherCandidate(hidden[cleared[candidate].Key], candidate))
                .OrderBy(entry => entry.Object.Id.Index),
        ];
    }

    private OnceBack ArrivingWhereTheyGoBack(List<OtherCandidate> others, PresenceInPass pass) =>
        ChangesOnceBack.Of(others.Select(entry => (cleared[entry.Candidate], entry.Object, pass.EndsRemoved(cleared[entry.Candidate].Key))), collected.Targets);

    /// <summary>With KeepSupportingObject: each candidate whose leaving leaves an object without support, with the first such object in id order.</summary>
    /// <param name="heldUp">The objects found held up, in id order, each with what started it.</param>
    private static Dictionary<int, RecordKey> HoldingUpOf(List<(OtherObject Object, FollowUpStarters.Started Started)> heldUp)
    {
        var holdingUp = new Dictionary<int, RecordKey>();
        foreach (var (held, started) in heldUp)
        {
            foreach (var candidate in started.Candidates) holdingUp.TryAdd(candidate, held.Key);
        }
        return holdingUp;
    }

    private HashSet<RecordKey> TargetsDecidedOf(FollowUpOutcomes outcomes) => [.. outcomes.Targets.Select(followed => collected.Targets[followed.Target.Index].Key)];

    private List<RestoredByFollowUp> TargetsRestoredOf(RestoreFollowUp written) =>
    [
        .. written.Targets.Where(followed => followed.RestoredTo is not null)
            .Select(followed => new RestoredByFollowUp(collected.Targets[followed.Target.Index].RestoredAs(followed.RestoredTo!), followed.StartedBy)),
    ];

    /// <summary>The arrivals the follow-up may not pull down or remove: the leaving candidates among them, and the target objects it restores.</summary>
    private static RefusedArrivals ArrivalsRefused(FollowUpOutcomes outcomes, Dictionary<TargetId, int> targetsLeaving, Dictionary<OtherId, int> othersLeaving)
    {
        var refused = new RefusedArrivals([], []);
        foreach (var arrival in outcomes.Arriving.Where(arrival => arrival.Outcome is null))
        {
            if (LeavingCandidateOf(arrival.Arriving, targetsLeaving, othersLeaving) is { } candidate) refused.Candidates.Add(candidate, arrival.Misfit);
            else refused.Targets.Add(arrival);
        }
        return refused;
    }

    /// <returns>The candidate leaving that an object arriving somewhere stands for; null for a target object the follow-up restores.</returns>
    private static int? LeavingCandidateOf(Supporter arriving, Dictionary<TargetId, int> targetsLeaving, Dictionary<OtherId, int> othersLeaving) => arriving.Type switch
    {
        SupporterType.Target => targetsLeaving.TryGetValue(new TargetId(arriving.Index), out var candidate) ? candidate : null,
        SupporterType.PlacedObject => othersLeaving[new OtherId(arriving.Index)],
        _ => throw new UnreachableException($"The follow-up found {arriving.Type} arriving."),
    };

    /// <summary>The candidates that start the follow-up's restore of each target object that does not fit where it arrives, each with the first such object in target order.</summary>
    private Dictionary<int, RecordKey> TakingAlongOf(List<ArrivingWhereItDoesNotFit> targetsArriving, FollowUpStarters starters)
    {
        var takingAlong = new Dictionary<int, RecordKey>();
        foreach (var arrival in targetsArriving)
        {
            var key = collected.Targets[arrival.Arriving.Index].Key;
            foreach (var candidate in starters.StartersOf([arrival.Arriving], key).Candidates.Order()) takingAlong.TryAdd(candidate, key);
        }
        return takingAlong;
    }

    /// <summary>The entry never judges a leaving candidate: a target one is decided, any other one left out, so an outcome naming one is a bug.</summary>
    private static void ThrowOnLeavingCandidateFollowed(FollowUpOutcomes outcomes, Dictionary<TargetId, int> targetsLeaving, Dictionary<OtherId, int> othersLeaving)
    {
        if (outcomes.Targets.FirstOrDefault(followed => targetsLeaving.ContainsKey(followed.Target)) is { } target)
        {
            throw new UnreachableException($"The follow-up decided target object {target.Target.Index}, which goes back.");
        }
        var judged = outcomes.Others.Select(other => other.Outcome.Object).Concat(outcomes.HeldUp.Select(other => other.Object));
        foreach (var other in judged.Where(other => othersLeaving.ContainsKey(other.Id)))
        {
            throw new UnreachableException($"The follow-up judged {other.Key}, which goes back.");
        }
    }

    /// <param name="copies">The candidates going back with a copy that are no target, each with the object its copy stands for.</param>
    private HashSet<int> CopiesLost(FollowUpOutcomes outcomes, IReadOnlySet<int> withClone, List<OtherCandidate> copies)
    {
        var targetsTaken = outcomes.Targets.Select(followed => followed.Target).ToHashSet();
        var objectsTaken = outcomes.Copies.Select(followed => followed.Object.Id).ToHashSet();
        return
        [
            .. withClone.Where(candidate => cleared[candidate].Target is { } target && targetsTaken.Contains(target)),
            .. copies.Where(entry => objectsTaken.Contains(entry.Object.Id)).Select(entry => entry.Candidate),
        ];
    }

    /// <summary>
    /// The target objects decided and the objects of any plugin left without support but those still resting where they stand, each with
    /// its starters, then the candidates pulled down or removed where they arrive.
    /// </summary>
    /// <remarks>A candidate pulled down or removed where it arrives is started by itself.</remarks>
    private RestoreFollowUp Written(FollowUpOutcomes outcomes, FollowUpStarters starters) =>
        new(
            [.. outcomes.Targets.Select(followed => WrittenTarget(followed, starters.StartersOf(followed.LostFrom, collected.Targets[followed.Target.Index].Key)))],
            [
                .. outcomes.Others.Where(other => other.Outcome.Fate != OtherModsObjectFate.LeftStillTouching)
                    .Select(other => WrittenOther(other.Outcome, starters.StartersOf(other.LostFrom, other.Outcome.Object.Key)))
                    .Concat(outcomes.Arriving.Where(arrival => arrival.Outcome is not null)
                        .Select(arrival => WrittenOther(arrival.Outcome!, starters.StartersOf([arrival.Arriving], arrival.Outcome!.Object.Key))))
                    .OrderBy(other => other.Outcome.Object.Id.Index),
            ]);

    private FollowUpTarget WrittenTarget(FollowedTarget followed, FollowUpStarters.Started started) =>
        new(followed.Target, RestoredTo(followed), KeysOf(started)) { ByGround = started.ByGround };

    private FollowUpOtherObject WrittenOther(OtherModsObjectOutcome outcome, FollowUpStarters.Started started) =>
        new(outcome, KeysOf(started)) { ByGround = started.ByGround };

    /// <returns>Null for a target object the follow-up removes.</returns>
    private static EarlierVersion? RestoredTo(FollowedTarget followed) => followed.Decision switch
    {
        Decision.Restored restored => restored.EarlierVersion,
        Decision.Removed => null,
        var other => throw new UnreachableException($"The follow-up decided {other} for target object {followed.Target.Index}."),
    };

    /// <returns>The keys of the candidates, in key order.</returns>
    private ImmutableArray<RecordKey> KeysOf(FollowUpStarters.Started started) =>
        [.. started.Candidates.Select(candidate => cleared[candidate].Key).Order(RecordKeyOrder.Comparer)];

    /// <summary>Each of the candidates that stands now as an object of any plugin, as that object where it stands now, in id order.</summary>
    private List<OtherCandidate> StandingOthersOf(IEnumerable<int> candidates) =>
    [
        .. candidates.Select(candidate => (Object: StandingNow(cleared[candidate].Key), Candidate: candidate))
            .Where(entry => entry.Object is not null)
            .Select(entry => new OtherCandidate(entry.Object!.Value, entry.Candidate))
            .OrderBy(entry => entry.Object.Id.Index),
    ];

    /// <returns>Null when the run has no such object, or removed it.</returns>
    private OtherObject? StandingNow(RecordKey key)
    {
        if (!_onceBack.Value.OthersByKey.TryGetValue(key, out var other)) return null;
        return othersTakenAway.TryGetValue(other.Id, out var after) ? after : other;
    }

    /// <returns>
    /// The decisions with the target candidates leaving restored in one new round, or removed when they end removed; the run's decisions
    /// when none leaves.
    /// </returns>
    private IRemovalDecisions Decided(Dictionary<TargetId, int> targetsLeaving, PresenceInPass pass) =>
        targetsLeaving.Count == 0
            ? final
            : final.WorkingCopy().RecordGoingBack(
            [
                .. targetsLeaving.Select(entry => new GoingBack(
                    entry.Key, new GoingBackWhereRemoved(), pass.EndsRemoved(cleared[entry.Value].Key) ? null : cleared[entry.Value].Earlier)),
            ]);
}
