using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The follow-up of objects leaving outside the removal rounds (see <see cref="IFollowUpOfObjectsLeaving"/>), by the also-remove
/// rounds (<see cref="IAlsoRemoveRoundsAfter"/>), the rule for target objects on objects leaving (<see cref="TargetsOnTakenAwayObjects"/>),
/// the same rule for the objects copies stand for (<see cref="CopiesGoingWithWhatLeaves"/>) and the rule for objects left without
/// support (<see cref="LeftWithoutSupport"/>), as <see cref="LeftWithoutSupportRounds"/> asks them. Only the objects of plugins
/// trusted in full (<see cref="OtherModsObjectRules.IsJudgedWhenObjectsLeave"/>) other than the objects copies stand for are judged
/// for their support, and only from what leaves: the world before is the one the leaving starts from, the run's own outcomes for
/// objects of any plugin included, so nothing the run already changed counts as a loss. The worlds after stand on the ground of
/// <see cref="ObjectsLeaving.Land"/>, and where it goes down is judged from too, so a judged object resting where the ground under it
/// goes down loses the terrain's support; the world before and the target rounds keep the ground as it is. An object that cannot hold
/// up others leaves nothing behind, and one arriving in another shape stands there in that shape in the world after, as one moved
/// stands where it arrives, so what still rests on it there loses nothing. Last, what arrives somewhere is judged where it arrives
/// (<see cref="ArrivalsThatDoNotFit"/>), against the world before the cleaned mod's changes, its ground included, and the run's own
/// outcomes, so what those took away counts as lost there, and ground and water lines the cleaned mod made that stay count against it.
/// Each arrival is judged in the world as the arrivals settled before it left it, and pulling down or removing an arrival takes no
/// target object along. Thread-safe.
/// </summary>
internal sealed class FollowUpOfObjectsLeaving(SupportJudging judging, AlsoRemoveSettings alsoRemove, IAlsoRemoveRoundsAfter alsoRemoveRounds) : IFollowUpOfObjectsLeaving
{
    /// <summary>What one question reads besides the decisions.</summary>
    /// <param name="Others">The objects of any plugin as the run leaves them.</param>
    /// <param name="Leaving">The objects of any plugin leaving that can hold up others, in id order.</param>
    /// <param name="Copies">The objects copies stand for that are judged, in id order; none with the also-remove mode Nothing.</param>
    /// <param name="IsJudged">Which objects of any plugin are judged for losing their support.</param>
    /// <param name="InAnotherShape">
    /// The objects arriving in another shape that can hold up others, as they stand once arrived, in id order: in every world after
    /// they stand there, so what rests on one that keeps its place in another shape is judged against that shape.
    /// </param>
    /// <param name="GroundAfter">The ground of the world after.</param>
    /// <param name="Lowered">Where the ground of the world after lies lower than the ground as it is.</param>
    /// <param name="GroundWithoutCleanedMod">The ground the arrivals are measured from.</param>
    /// <param name="WaterLinesThatStay">The water lines the cleaned mod moved that stay moved.</param>
    private sealed record Scene(
        IVisibleObjectsOfAnyPlugin Others,
        ImmutableArray<OtherLeaving> Leaving,
        ImmutableArray<OtherObject> Copies,
        OtherModsObjectRules Rules,
        Func<OtherObject, bool> IsJudged,
        ImmutableArray<OtherObject> InAnotherShape,
        ITerrainHeights GroundAfter,
        ImmutableArray<BoxInSpace> Lowered,
        ITerrainHeights GroundWithoutCleanedMod,
        ImmutableArray<MovedWaterLine> WaterLinesThatStay)
    {
        public IReadOnlyDictionary<OtherId, OtherObject> InAnotherShapeById { get; } = InAnotherShape.ToDictionary(other => other.Id);
    }

    /// <summary>What goes with the target objects that left and the objects leaving, besides target objects.</summary>
    private sealed record Judged(ImmutableArray<FollowedCopy> Copies, ImmutableArray<FollowedOther> Others, ImmutableArray<HeldUpOther> HeldUp);

    public FollowUpOutcomes Find(WorldStanding standing, IRemovalDecisions decided, ObjectsLeaving leaving)
    {
        var scene = SceneOf(standing, leaving);
        var targets = decided.TargetsAsTheRoundsMeasureThem(judging.Targets);
        var copy = decided.WorkingCopy();
        var details = new List<RoundDetails?>();
        var current = FollowTargets(copy, standing.Decisions, decided, scene, targets, details);
        var before = WorldOf(standing.Decisions, scene);
        var weightsBefore = new WeightsBefore([]);
        while (true)
        {
            var judged = JudgeCopiesAndOthers(standing.Decisions, current, before, scene, weightsBefore);
            var next = alsoRemove.Mode == FollowUpRemovalMode.Nothing ? current : DecideOnLeaving(copy, current, Leaving(scene, judged.Copies, judged.Others), targets, scene, details);
            if (next.RoundsAfter(current).All(round => next.LeftIn(round).IsEmpty))
            {
                return Summarize(decided, next, details, judged, FindArrivals(standing, decided, next, judged, scene, leaving));
            }
            current = next;
        }
    }

    /// <summary>
    /// The target objects restored on <paramref name="decided"/> or by the follow-up's rounds and the objects leaving or arriving in
    /// another shape that can rest, each where it arrives, that do not fit there between the world in which every object that could
    /// leave did, on the ground without the cleaned mod's changes, and the world of <paramref name="current"/> on the planned ground.
    /// </summary>
    private ImmutableArray<ArrivingWhereItDoesNotFit> FindArrivals(WorldStanding standing, IRemovalDecisions decided, IRemovalDecisions current, Judged judged, Scene scene, ObjectsLeaving leaving)
    {
        var restored = TargetsLeftSince(standing.Decisions, current).Where(target => current.Of(target) is Decision.Restored).ToHashSet();
        List<OtherLeaving> moving = [.. leaving.Others.Where(other => other.To is not null && scene.Rules.CanRest(other.Object))];
        List<OtherObject> reshaped = [.. leaving.ArrivingInAnotherShape.Where(scene.Rules.CanRest)];
        if (restored.Count == 0 && moving.Count == 0 && reshaped.Count == 0) return [];

        List<ArrivalsThatDoNotFit.Arrival> arrivals =
        [
            .. WhereTheyStand(current, restored, scene).Select(target => new ArrivalsThatDoNotFit.Arrival(scene.Rules.StandingOf(target), Other: null)),
            .. moving.Select(other => new ArrivalsThatDoNotFit.Arrival(scene.Rules.StandingOf(other.Object, other.To!.Value), other.Object with { Position = other.To!.Value.Position })),
            .. reshaped.Select(other => new ArrivalsThatDoNotFit.Arrival(scene.Rules.StandingOf(other), other)),
        ];
        var moved = MovedOrReshaped(scene, leaving);
        var everyoneLeft = WorldWithEveryTargetGoneBack(decided, leaving)
            .With(moved.Concat(leaving.Staying.Where(other => scene.Rules.CanHoldUp(other.Object)).Select(other => KeyValuePair.Create(other.Object.Id, other.To))).ToDictionary())
            .WithAdded([.. leaving.StayingInAnotherShape.Where(scene.Rules.CanHoldUp).Concat(scene.InAnotherShape).OrderBy(other => other.Id.Index)])
            .OnGround(scene.GroundWithoutCleanedMod);
        return new ArrivalsThatDoNotFit(
                everyoneLeft,
                WorldOf(current, scene).OnGround(scene.GroundAfter),
                MovedAndTakenAway(moved, judged, scene),
                leaving.ArrivingInAnotherShape,
                scene.WaterLinesThatStay,
                scene.Rules,
                SupportJudgingParts.CreateSupport(judging),
                SupportJudgingParts.CreateSupportLoss(judging),
                SupportJudgingParts.CreateGroundChange(judging),
                SupportJudgingParts.CreateDropFinder(judging, judging.Settings.TouchGap),
                judging.Settings)
            .Find(arrivals);
    }

    /// <summary>
    /// The objects leaving that can hold up others, each where it arrives, and those arriving in another shape, taken out where they
    /// stand now in that shape: they stand only as one of <see cref="ObjectsLeaving.ArrivingInAnotherShape"/>.
    /// </summary>
    private static Dictionary<OtherId, PlacedTransform?> MovedOrReshaped(Scene scene, ObjectsLeaving leaving)
    {
        var moved = scene.Leaving.ToDictionary(other => other.Object.Id, other => other.To);
        foreach (var other in leaving.ArrivingInAnotherShape) moved[other.Id] = null;
        return moved;
    }

    /// <summary>The target objects of <paramref name="ids"/> that can hold up others, where <paramref name="decisions"/> places them, in target order.</summary>
    private List<TargetObject> WhereTheyStand(IRemovalDecisions decisions, HashSet<TargetId> ids, Scene scene) =>
        [.. decisions.TargetsAsTheyStand(judging.Targets).Where(target => ids.Contains(target.Id) && scene.Rules.SupportingBoxOf(target) is not null)];

    /// <summary>
    /// The world of <paramref name="decided"/> with each target object of <see cref="ObjectsLeaving.TargetsStaying"/> at the version it
    /// would go back to, whatever the run decided for it, and gone when that version stands nowhere; the objects of any plugin as they
    /// stood before the run took any away.
    /// </summary>
    private SupportWorld WorldWithEveryTargetGoneBack(IRemovalDecisions decided, ObjectsLeaving leaving)
    {
        var goneBack = leaving.TargetsStaying.ToDictionary(entry => entry.Target, entry => entry.To);
        ImmutableArray<TargetObject> placed =
        [
            .. decided.TargetsAsTheyStand(judging.Targets)
                .Select(target => goneBack.GetValueOrDefault(target.Id) is { } earlier ? judging.Targets[target.Id.Index].RestoredAs(earlier) : target),
        ];
        return new SupportWorld(
            placed,
            judging.Caches.IndexVisibleTargets(placed),
            target => goneBack.TryGetValue(target, out var to) ? to is not null : !decided.IsRemoved(target),
            judging.Caches.VisibleObjectsOfAnyPlugin(),
            SupportWorld.NoChanges,
            judging.Caches.Shapes,
            judging.Caches.Terrain);
    }

    /// <summary>The objects leaving where they arrive, and the objects copies stand for and the judged objects the follow-up took away: removed, or where they land.</summary>
    private static Dictionary<OtherId, PlacedTransform?> MovedAndTakenAway(Dictionary<OtherId, PlacedTransform?> moved, Judged judged, Scene scene)
    {
        var changes = new Dictionary<OtherId, PlacedTransform?>(moved);
        foreach (var copy in judged.Copies.Where(copy => scene.Rules.CanHoldUp(copy.Object))) changes[copy.Object.Id] = null;
        foreach (var outcome in judged.Others.Select(other => other.Outcome).Where(outcome => outcome.IsTakenAway))
        {
            changes[outcome.Object.Id] = outcome.IsRemoved ? null : outcome.Object.Transform with { Position = outcome.NewPosition };
        }
        return changes;
    }

    private Scene SceneOf(WorldStanding standing, ObjectsLeaving leaving)
    {
        var rules = SupportJudgingParts.CreateRules(judging);
        var copyIds = leaving.Copies.Select(other => other.Id).ToHashSet();
        return new Scene(
            judging.Caches.VisibleObjectsOfAnyPluginAsTheyStand(standing.OthersTakenAway),
            [.. leaving.Others.Where(other => rules.CanHoldUp(other.Object))],
            alsoRemove.Mode == FollowUpRemovalMode.Nothing ? [] : [.. leaving.Copies.Where(rules.CanRest)],
            rules,
            other => !copyIds.Contains(other.Id) && rules.IsJudgedWhenObjectsLeave(other),
            [.. leaving.ArrivingInAnotherShape.Where(rules.CanHoldUp)],
            leaving.Land?.Planned ?? judging.Caches.Terrain,
            [.. (leaving.Land?.Lowered ?? []).Select(lowered => new BoxInSpace(lowered.Space, lowered.Box))],
            leaving.Land?.WithoutCleanedMod ?? judging.Caches.Terrain,
            leaving.Land?.WaterLinesThatStay ?? []);
    }

    /// <summary>The also-remove rounds from the target objects leaving, then from the objects of any plugin leaving.</summary>
    private IRemovalDecisions FollowTargets(
        IRemovalDecisionsCopy copy, IRemovalDecisions standing, IRemovalDecisions decided, Scene scene, ImmutableArray<TargetObject> targets, List<RoundDetails?> details)
    {
        if (alsoRemove.Mode == FollowUpRemovalMode.Nothing) return decided;

        var spread = DecideSpread(copy, decided, [.. TargetsLeftSince(standing, decided)], targets, details);
        return DecideOnLeaving(copy, spread, scene.Leaving, targets, scene, details);
    }

    /// <summary>The also-remove rounds from <paramref name="seeds"/>, removed in <paramref name="current"/>, until a round removes nothing.</summary>
    private IRemovalDecisions DecideSpread(
        IRemovalDecisionsCopy copy, IRemovalDecisions current, ImmutableArray<TargetId> seeds, ImmutableArray<TargetObject> targets, List<RoundDetails?> details)
    {
        if (seeds.IsEmpty) return current;
        var (spread, rounds) = copy.DecideRoundsUntilNothingRemoved(AlsoRemoveRounds.Opening, alsoRemoveRounds.After(current, targets), seeds);
        details.AddRange(rounds.Select(round => (RoundDetails?)round.Details));
        return spread;
    }

    /// <summary>One round for the target objects on the objects leaving where they stood, then the rounds that spread from it.</summary>
    /// <param name="leaving">In id order.</param>
    private IRemovalDecisions DecideOnLeaving(
        IRemovalDecisionsCopy copy, IRemovalDecisions current, IReadOnlyList<OtherLeaving> leaving, ImmutableArray<TargetObject> targets, Scene scene, List<RoundDetails?> details)
    {
        if (leaving.Count == 0) return current;
        var proposals = TargetsOnLeaving(targets, scene).Propose(current, leaving);
        if (proposals.Count == 0) return current;

        current = copy.DecideRound(AlsoRemoveRounds.Opening, proposals);
        details.Add(null);
        return DecideSpread(copy, current, current.LeftIn(current.Rounds[^1]), targets, details);
    }

    private TargetsOnTakenAwayObjects TargetsOnLeaving(ImmutableArray<TargetObject> targets, Scene scene)
    {
        var visible = judging.Caches.IndexVisibleTargets(targets);
        return new(
            targets,
            visible,
            new SupportWorld(targets, visible, _ => true, scene.Others, SupportWorld.NoChanges, judging.Caches.Shapes, judging.Caches.Terrain),
            SupportJudgingParts.CreateSupport(judging),
            scene.Rules,
            judging.Caches.Triangles,
            alsoRemove,
            judging.Execution,
            scene.InAnotherShapeById);
    }

    /// <summary>
    /// The target objects where <paramref name="decisions"/> places those that stand, and the objects of any plugin as the run leaves
    /// them, on the ground as it is.
    /// </summary>
    private SupportWorld WorldOf(IRemovalDecisions decisions, Scene scene)
    {
        var placed = decisions.TargetsAsTheyStand(judging.Targets);
        return new SupportWorld(
            placed,
            judging.Caches.IndexVisibleTargets(placed),
            target => !decisions.IsRemoved(target),
            scene.Others,
            SupportWorld.NoChanges,
            judging.Caches.Shapes,
            judging.Caches.Terrain);
    }

    /// <summary>
    /// The objects copies stand for that go with the target objects left since <paramref name="standing"/> and the objects leaving, and
    /// the judged objects that lose their support then, each judged with the other's taken along, until no further copy goes.
    /// </summary>
    /// <param name="before">The world of <paramref name="standing"/>.</param>
    private Judged JudgeCopiesAndOthers(IRemovalDecisions standing, IRemovalDecisions current, SupportWorld before, Scene scene, WeightsBefore weightsBefore)
    {
        var targetsLeft = TargetsLeftSince(standing, current).ToHashSet();
        var targetsWhereTheyStood = WhereTheyStand(standing, targetsLeft, scene);
        var after = WorldOf(current, scene).WithAdded(scene.InAnotherShape).OnGround(scene.GroundAfter);
        var copies = ImmutableArray<FollowedCopy>.Empty;
        while (true)
        {
            var (others, heldUp) = JudgeOthers(targetsLeft, targetsWhereTheyStood, before, after, scene, Leaving(scene, copies, []), weightsBefore);
            var found = FindCopies(targetsWhereTheyStood, before, scene, Leaving(scene, copies, others));
            var known = copies.Select(followed => followed.Object.Id).ToHashSet();
            if (found.All(followed => known.Contains(followed.Object.Id))) return new Judged(copies, others, heldUp);
            copies = [.. copies.Concat(found.Where(followed => !known.Contains(followed.Object.Id))).OrderBy(followed => followed.Object.Id.Index)];
        }
    }

    private ImmutableArray<FollowedCopy> FindCopies(List<TargetObject> targetsWhereTheyStood, SupportWorld before, Scene scene, List<OtherLeaving> leaving) =>
        scene.Copies.IsEmpty
            ? []
            : new CopiesGoingWithWhatLeaves(
                    before, SupportJudgingParts.CreateSupport(judging), scene.Rules, judging.Caches.Triangles, alsoRemove, judging.Execution, scene.InAnotherShapeById)
                .Find(scene.Copies, targetsWhereTheyStood, leaving);

    /// <summary>
    /// The judged objects that lose their support between <paramref name="before"/> and <paramref name="after"/> once
    /// <paramref name="leaving"/> left, judged from the boxes of the target objects that left and of the objects leaving, where they
    /// stood, and of the ground that goes down: with KeepSupportingObject only found, as held up; otherwise left without support.
    /// </summary>
    /// <param name="targetsWhereTheyStood">The target objects that left and could hold up others, where they stood.</param>
    private (ImmutableArray<FollowedOther> Others, ImmutableArray<HeldUpOther> HeldUp) JudgeOthers(
        HashSet<TargetId> targetsLeft, List<TargetObject> targetsWhereTheyStood, SupportWorld before, SupportWorld after, Scene scene, List<OtherLeaving> leaving, WeightsBefore weightsBefore)
    {
        List<BoxInSpace> changed =
        [
            .. targetsWhereTheyStood.Select(target => new BoxInSpace(target.SpaceKey, scene.Rules.BoxOf(target))),
            .. leaving.Select(other => new BoxInSpace(other.Object.SpaceKey, scene.Rules.BoxOf(other.Object, other.Object.Transform))),
            .. scene.Lowered,
        ];
        var changes = leaving.ToDictionary(other => other.Object.Id, other => other.To);
        var settler = SupportJudgingParts.CreateLeftWithoutSupport(judging, before, after, scene.Others, scene.Rules, scene.IsJudged, weightsBefore);
        if (judging.Settings.Handling == OtherModsObjectsHandling.KeepSupportingObject)
        {
            var heldUp = settler.FindLosingSupport(changed, changes);
            var heldUpLost = FindLostFrom(heldUp, before, scene, targetsLeft, [.. changes.Keys]);
            return ([], [.. heldUp.Select((other, k) => new HeldUpOther(other, heldUpLost[k]))]);
        }

        var outcomes = settler.Settle(changed, changes);
        var othersLeft = changes.Keys.Concat(outcomes.Where(outcome => outcome.IsTakenAway).Select(outcome => outcome.Object.Id)).ToHashSet();
        var lostFrom = FindLostFrom([.. outcomes.Select(outcome => outcome.Object)], before, scene, targetsLeft, othersLeft);
        return ([.. outcomes.Select((outcome, k) => new FollowedOther(outcome, lostFrom[k]))], []);
    }

    /// <param name="othersLeft">The objects of any plugin that left their place.</param>
    /// <returns>
    /// For each object, what held it up in <paramref name="before"/> and left its place, the terrain where the ground under it goes
    /// down included, largest share first.
    /// </returns>
    private ImmutableArray<ImmutableArray<Supporter>> FindLostFrom(
        IReadOnlyList<OtherObject> objects, SupportWorld before, Scene scene, HashSet<TargetId> targetsLeft, HashSet<OtherId> othersLeft)
    {
        List<StandingObject> standing = [.. objects.Select(scene.Rules.StandingOf)];
        var support = SupportJudgingParts.CreateSupport(judging);
        var ground = SupportJudgingParts.CreateGroundChange(judging);
        return
        [
            .. ParallelMap.Run(
                judging.Execution,
                standing.Count,
                () => new OtherObjectQueries(),
                (k, queries) => SupporterGone.ThatLeft(
                        support.FindContacts(standing[k], before, queries),
                        targetsLeft.Contains,
                        othersLeft.Contains,
                        isTerrainGone: !scene.Lowered.IsEmpty && ground.GoesDownUnder(standing[k], before.Terrain, scene.GroundAfter))
                    .Select(entry => entry.Supporter)
                    .ToImmutableArray(),
                ParallelMap.AutomaticRangeSize),
        ];
    }

    /// <summary>The target objects removed or restored in the rounds <paramref name="later"/> has beyond <paramref name="earlier"/>, in round order.</summary>
    private static IEnumerable<TargetId> TargetsLeftSince(IRemovalDecisions earlier, IRemovalDecisions later) =>
        later.RoundsAfter(earlier).SelectMany(round => later.LeftIn(round));

    /// <summary>The objects leaving, then the objects copies stand for taken along and the objects removed or pulled down, taken away where they stood, in id order.</summary>
    private static List<OtherLeaving> Leaving(Scene scene, IEnumerable<FollowedCopy> copies, IEnumerable<FollowedOther> others) =>
    [
        .. scene.Leaving
            .Concat(copies.Select(copy => copy.Object).Where(scene.Rules.CanHoldUp).Select(copy => new OtherLeaving(copy, To: null)))
            .Concat(others.Select(other => other.Outcome).Where(outcome => outcome.IsTakenAway).Select(outcome => new OtherLeaving(outcome.Object, To: null)))
            .OrderBy(other => other.Object.Id.Index),
    ];

    /// <param name="details">Each new round's details in round order; null for a round on objects leaving, whose reasons name them.</param>
    private static FollowUpOutcomes Summarize(
        IRemovalDecisions decided, IRemovalDecisions current, List<RoundDetails?> details, Judged judged, ImmutableArray<ArrivingWhereItDoesNotFit> arriving)
    {
        var rounds = current.RoundsAfter(decided);
        if (rounds.Length != details.Count) throw new UnreachableException($"The follow-up decided {rounds.Length} rounds but recorded details of {details.Count}.");
        var detailsOf = rounds.Zip(details).ToDictionary(entry => entry.First, entry => entry.Second);
        return new FollowUpOutcomes(
            [
                .. rounds.SelectMany(round => current.LeftIn(round))
                    .Order()
                    .Select(target => (Target: target, Decision: current.Of(target)!))
                    .Select(entry => new FollowedTarget(entry.Target, entry.Decision, LostFrom(entry.Target, entry.Decision, detailsOf[entry.Decision.Round]))),
            ],
            judged.Copies,
            judged.Others,
            judged.HeldUp,
            arriving);
    }

    private static ImmutableArray<Supporter> LostFrom(TargetId target, Decision decision, RoundDetails? details) => decision.Reason switch
    {
        LinkedTo linked => [Supporter.Target(linked.To.Index)],
        TouchingTargetThatLeft touching => [Supporter.Target(touching.Touched.Index)],
        LostSupport => SupportersThatLeft(target, details),
        TouchingAnotherModsObject touching => [Supporter.Placed(touching.Touched)],
        LostSupportWithAnotherModsObject lost => [Supporter.Placed(lost.MainSupporter)],
        var reason => throw new UnreachableException($"The follow-up decided target {target.Index} for {reason.GetType().Name}."),
    };

    /// <summary>The target objects that left their place and held its support, largest share first.</summary>
    private static ImmutableArray<Supporter> SupportersThatLeft(TargetId target, RoundDetails? details) =>
    [
        .. ((SupportRoundDetails)details!).Evaluations.Single(evaluation => evaluation.TargetIndex == target.Index)
            .Shares.Where(share => SupporterShares.HasLeftItsPlace(share.Category))
            .Select(share => share.Supporter),
    ];
}
