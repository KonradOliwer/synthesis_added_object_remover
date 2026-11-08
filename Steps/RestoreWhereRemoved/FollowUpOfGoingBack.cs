using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Asks the removal steps' follow-up (<see cref="IFollowUpOfObjectsLeaving"/>) about the candidates leaving where they stand now. Thread-safe.
/// <list type="bullet">
/// <item>A target candidate leaves as restored to its own earlier version on a working copy of the run's decisions (removed when that
/// version stands nowhere).</item>
/// <item>Any other candidate leaves as an object of any plugin with the parts that go back taken from its earlier version: taken away
/// when it ends removed, and arriving anew when those parts give it another shape. A candidate the game shows again only arrives.</item>
/// <item>A candidate going back with a copy does not leave: the copy stands there, as the object of the run, and is judged like the
/// cleaned mod's own objects, so the copy is lost when the follow-up takes that object along.</item>
/// <item>The follow-up takes along target objects, copies and objects of plugins trusted in full only; the objects of every other
/// plugin stay as they are.</item>
/// <item>What the follow-up decides is written, so it keeps no candidate in place, but with KeepSupportingObject, where an object of a
/// plugin trusted in full the leaving would leave without support keeps the candidates it started from where they stand now, as the
/// removal steps keep a supporting object.</item>
/// <item>A candidate that would not fit where it arrives is measured against the world in which the candidates kept in place and every
/// other placed record and every height the cleaned mod changed went back too (only what the cleaned mod, the run or the restore took
/// away counts, not what an unrelated mod changed; the ground and water lines the cleaned mod made that stay are its content). It is
/// pulled down or removed there when it loses its support and is an object of a plugin trusted in full and not a target; any other one
/// is kept where it stands now, so it never floats, ends in the ground or at a water line the cleaned mod moved.</item>
/// <item>A target object the follow-up restores that would not fit where it goes back keeps the candidates it started from where they
/// stand now.</item>
/// <item>The ground the pass plans is the ground of the world after: an object of a plugin trusted in full resting on ground that goes
/// down is followed up as one resting on a candidate, and with KeepSupportingObject the ground under it stays; every other object
/// stays as it is, also when ground goes down.</item>
/// </list>
/// </summary>
/// <param name="final">The decisions with every round and every move.</param>
/// <param name="othersTakenAway">The objects of any plugin the run pulled down (where they stand now) or removed (null), by id.</param>
/// <param name="cleared">The placed records whose areas let them go back, by candidate index.</param>
/// <param name="landUnder">The LAND record the cleaned mod changed under a point of a space; null where there is none.</param>
internal sealed class FollowUpOfGoingBack(
    IFollowUpOfObjectsLeaving rule,
    IRemovalDecisions final,
    IReadOnlyDictionary<OtherId, OtherObject?> othersTakenAway,
    IReadOnlyList<RecordChangedByCleanedMod> cleared,
    CollectedObjects collected,
    Func<RecordKey, Vector3, RecordKey?> landUnder)
{
    private readonly ComputedOnce<ChangesOnceBack> _onceBack = new(() => ChangesOnceBack.Of(cleared, collected));

    /// <summary>One instance for every pass, so the rule shares what it measures in that world between them.</summary>
    private readonly WorldStanding _standing = new(final, othersTakenAway);

    /// <summary>A candidate that is no target, as its object where it stands now or, for one the game shows again, as it stands once back.</summary>
    private readonly record struct OtherCandidate(OtherObject Object, int Candidate);

    /// <param name="Leaving">The candidates going back without a copy, in index order.</param>
    /// <param name="Targets">The candidates leaving that are targets, by target.</param>
    /// <param name="Others">The candidates leaving that are no targets and stand now, each as its object where it stands now.</param>
    /// <param name="ShownAgain">The candidates leaving that the game shows again, each as it stands once back.</param>
    /// <param name="Copies">The candidates going back with a copy that are no targets, each as its object where it stands now.</param>
    private sealed record LeavingCandidates(
        List<int> Leaving, Dictionary<TargetId, int> Targets, List<OtherCandidate> Others, List<OtherCandidate> ShownAgain, List<OtherCandidate> Copies)
    {
        public Dictionary<OtherId, int> OthersLeavingById() => Others.Concat(ShownAgain).ToDictionary(entry => entry.Object.Id, entry => entry.Candidate);
    }

    /// <param name="Candidates">The candidates leaving that do not fit where they arrive and stay where they stand now, each with why.</param>
    /// <param name="Targets">The target objects the follow-up restores that do not fit where they arrive.</param>
    private sealed record RefusedArrivals(Dictionary<int, ArrivalRefused> Candidates, List<ArrivingWhereItDoesNotFit> Targets);

    /// <param name="goingBack">The indexes of the candidates still going back.</param>
    /// <param name="withClone">Those of them going back with a copy.</param>
    /// <param name="pass">Whether a placed record ends removed with <paramref name="goingBack"/> going back.</param>
    /// <param name="land">The ground and water the pass plans; null for the ground and water as they are.</param>
    public FollowUpChecks Check(IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, PresenceInPass pass, LandAfter? land)
    {
        var candidates = SplitCandidates(goingBack, withClone, pass);
        var outcomes = rule.Find(_standing, Decided(candidates.Targets, pass), LeavingOf(goingBack, candidates, pass, land));
        return BuildChecks(outcomes, candidates, withClone);
    }

    private LeavingCandidates SplitCandidates(IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, PresenceInPass pass)
    {
        List<int> leaving = [.. goingBack.Where(candidate => !withClone.Contains(candidate)).Order()];
        return new LeavingCandidates(
            leaving,
            leaving.Where(candidate => cleared[candidate].Target is not null).ToDictionary(candidate => cleared[candidate].Target!.Value),
            StandingOthersOf(leaving.Where(candidate => cleared[candidate].Target is null)),
            ShownAgainOf(leaving, pass),
            StandingOthersOf(withClone.Where(candidate => cleared[candidate].Target is null)));
    }

    private FollowUpChecks BuildChecks(FollowUpOutcomes outcomes, LeavingCandidates candidates, IReadOnlySet<int> withClone)
    {
        var othersLeavingById = candidates.OthersLeavingById();
        ThrowOnLeavingCandidateFollowed(outcomes, candidates.Targets, othersLeavingById);
        var starters = FollowUpStarters.Of(outcomes, candidates.Targets, othersLeavingById);
        var written = Written(outcomes, starters);
        var refused = ArrivalsRefused(outcomes, candidates.Targets, othersLeavingById);
        var heldUp = HeldUpWithStarters(outcomes, starters);
        return new FollowUpChecks(HoldingUpOf(heldUp), CopiesLost(outcomes, withClone, candidates.Copies), written, TargetsDecidedOf(outcomes))
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
    private ObjectsLeaving LeavingOf(IReadOnlySet<int> goingBack, LeavingCandidates candidates, PresenceInPass pass, LandAfter? land)
    {
        List<int> staying = [.. Enumerable.Range(0, cleared.Count).Where(candidate => !goingBack.Contains(candidate))];
        var onceBack = _onceBack.Value;
        var arriving = ArrivingWhereTheyGoBack(candidates.Others, pass);
        var stayingArriving = ArrivingWhereTheyGoBack(StandingOthersOf(staying.Where(candidate => cleared[candidate].Target is null)), pass);
        return new ObjectsLeaving(
            [.. arriving.Leaving],
            [.. candidates.Copies.Select(entry => entry.Object)],
            [.. stayingArriving.Leaving.Concat(onceBack.NoCandidateOthers.Leaving).OrderBy(other => other.Object.Id.Index)],
            [
                .. staying.Where(candidate => cleared[candidate].Target is not null)
                    .Select(candidate => new GoingBack(cleared[candidate].Target!.Value, new GoingBackWhereRemoved(), cleared[candidate].Earlier))
                    .Concat(onceBack.NoCandidateTargets),
            ],
            [
                .. stayingArriving.InAnotherShape.Concat(onceBack.NoCandidateOthers.InAnotherShape).Concat(HiddenThatStayAway(candidates.Leaving))
                    .OrderBy(other => other.Id.Index),
            ],
            [.. arriving.InAnotherShape.Concat(candidates.ShownAgain.Select(entry => entry.Object)).OrderBy(other => other.Id.Index)],
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

    private static List<(OtherObject Object, FollowUpStarters.Started Started)> HeldUpWithStarters(FollowUpOutcomes outcomes, FollowUpStarters starters) =>
        [.. outcomes.HeldUp.Select(held => (held.Object, Started: starters.StartersOf(held.LostFrom, held.Object.Key)))];

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
    private RefusedArrivals ArrivalsRefused(FollowUpOutcomes outcomes, Dictionary<TargetId, int> targetsLeaving, Dictionary<OtherId, int> othersLeaving)
    {
        var refused = new RefusedArrivals([], []);
        foreach (var arrival in outcomes.Arriving.Where(arrival => arrival.Outcome is null))
        {
            if (LeavingCandidateOf(arrival.Arriving, targetsLeaving, othersLeaving) is { } candidate)
            {
                refused.Candidates.Add(candidate, new ArrivalRefused(arrival.Misfit, CauseKeysOf(arrival.Cause, cleared[candidate])));
            }
            else
            {
                refused.Targets.Add(arrival);
            }
        }
        return refused;
    }

    /// <summary>The records an arrival's cause names, in its order: the cell, each supporter's object, or the LAND record under where the candidate arrives.</summary>
    private ImmutableArray<RecordKey> CauseKeysOf(ArrivalCause cause, RecordChangedByCleanedMod arriving) =>
    [
        .. (cause.WaterLineCell is { } cell ? [cell] : Array.Empty<RecordKey>()),
        .. cause.LostSupports
            .Select(supporter => supporter.Type == SupporterType.Terrain ? landUnder(arriving.SpaceKey, ChangeJudge.WhereItEndsOf(arriving)) : KeyOf(supporter))
            .OfType<RecordKey>(),
    ];

    /// <summary>The key of a target or of an object of any plugin, one the game shows again included.</summary>
    private RecordKey? KeyOf(Supporter supporter) => supporter.Type switch
    {
        SupporterType.Target => collected.Targets[supporter.Index].Key,
        SupporterType.PlacedObject => OtherOf(new OtherId(supporter.Index))?.Key,
        _ => throw new UnreachableException($"The supporter {supporter.Type} has no key."),
    };

    /// <summary>An object of any plugin by its id: among the run's objects, or after them for one the game shows again; null for an id the follow-up gave another shape of.</summary>
    private OtherObject? OtherOf(OtherId id) =>
        id.Index < collected.OtherModObjects.Length + collected.SupportOnlyObjects.Length
            ? collected.Other(id)
            : _onceBack.Value.HiddenById.TryGetValue(id, out var hidden) ? hidden : null;

    /// <summary>The LAND records under the objects whose support ground going down took, in key order.</summary>
    /// <param name="rootSpace">The space of the object followed up.</param>
    /// <param name="rootPosition">Where the object followed up stands now.</param>
    private ImmutableArray<RecordKey> GroundLandsOf(FollowUpStarters.Started started, RecordKey rootSpace, Vector3 rootPosition) =>
    [
        .. started.OnGround
            .Select(member => member is not { } supporter ? landUnder(rootSpace, rootPosition) : LandUnder(supporter))
            .OfType<RecordKey>()
            .Distinct()
            .Order(RecordKeyOrder.Comparer),
    ];

    private RecordKey? LandUnder(Supporter supporter) => supporter.Type switch
    {
        SupporterType.Target => landUnder(collected.Targets[supporter.Index].SpaceKey, collected.Targets[supporter.Index].Transform.Position),
        SupporterType.PlacedObject => OtherOf(new OtherId(supporter.Index)) is { } other ? landUnder(other.SpaceKey, other.Position) : null,
        _ => throw new UnreachableException($"The supporter {supporter.Type} has no place."),
    };

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

    private FollowUpTarget WrittenTarget(FollowedTarget followed, FollowUpStarters.Started started)
    {
        var target = collected.Targets[followed.Target.Index];
        return new(followed.Target, RestoredTo(followed), KeysOf(started))
        {
            ByGround = started.ByGround, GroundLands = GroundLandsOf(started, target.SpaceKey, target.Transform.Position),
        };
    }

    private FollowUpOtherObject WrittenOther(OtherModsObjectOutcome outcome, FollowUpStarters.Started started) =>
        new(outcome, KeysOf(started)) { ByGround = started.ByGround, GroundLands = GroundLandsOf(started, outcome.Object.SpaceKey, outcome.Object.Position) };

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
