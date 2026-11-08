using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// What becomes of the other mods' objects left without support once the also-remove rounds are decided (see
/// <see cref="LeftWithoutSupport"/>), and the also-remove rounds for the target objects resting on or touching those taken away
/// (see <see cref="TargetsOnTakenAwayObjects"/>), then what goes with them; the other mods' objects are judged again against the
/// new decisions, until no further target object is removed: each pass removes one at least, so it ends. The rounds measure each
/// target object kept shrunk or turned at its new placement.
/// </summary>
/// <param name="alsoRemoveRounds">Asked only when <paramref name="alsoRemove"/> is not Nothing.</param>
internal sealed class LeftWithoutSupportRounds(SupportJudging judging, AlsoRemoveSettings alsoRemove, IAlsoRemoveRoundsAfter alsoRemoveRounds) : ILeftWithoutSupportRounds
{
    /// <remarks>The objects of any plugin are asked only when a target object was removed, shrunk or turned.</remarks>
    public OtherModsObjectsResult Settle(IRemovalDecisions afterFits, IRemovalDecisions decisions)
    {
        if (judging.Settings.Handling == OtherModsObjectsHandling.KeepSupportingObject) return OtherModsObjectsResult.SwitchedOff;

        var targetsAsMeasured = afterFits.TargetsAsTheRoundsMeasureThem(judging.Targets);
        var visibleTargetsAsMeasured = judging.Caches.IndexVisibleTargets(targetsAsMeasured);
        var targetsDecided = new DecidedTargets(
            judging.Targets, targetsAsMeasured, decisions, judging.Caches.VisibleTargets, visibleTargetsAsMeasured, placed => judging.Caches.IndexVisibleTargets(placed));
        return Failures.Attempt(
            () => Settle(targetsDecided, decided => alsoRemoveRounds.After(decided, targetsAsMeasured)),
            failure => new OtherModsObjectsResult(StepOutcome.StoppedByUserDataFailure, [], [], [], PartFailure.OfWholePart(OtherModsObjectsParts.LeftWithoutSupport, failure)));
    }

    /// <remarks>Without a changed target object nothing can have lost its support, so the objects of any plugin are not asked for.</remarks>
    private OtherModsObjectsResult Settle(DecidedTargets decided, Func<IRemovalDecisions, IRemovalRounds<RoundDetails>> alsoRemoveRoundsAfter)
    {
        var rules = SupportJudgingParts.CreateRules(judging);
        if (FindChangedTargets(decided.Targets, decided.Decided, rules).Count == 0) return new OtherModsObjectsResult(StepOutcome.Performed, [], [], [], Failure: null);

        var others = judging.Caches.VisibleObjectsOfAnyPlugin();
        var before = new WeightsBefore(new());
        var copy = decided.Decided.WorkingCopy();
        var current = decided.Decided;
        var standing = new StandingTargets(decided.AsMeasured, decided.VisibleAsMeasured, RoundsChecked: 0);
        var rounds = new List<IReadOnlyList<ProposedRemoval>>();
        while (true)
        {
            standing = StandingAfter(standing, current, decided);
            var outcomes = SettleAgainst(current, standing, decided, others, rules, before);
            var proposals = alsoRemove.Mode == FollowUpRemovalMode.Nothing
                ? []
                : CreateTargetsOnTakenAway(decided, others, rules).Propose(current, TakenAway(outcomes));
            if (proposals.Count == 0) return Summarize(outcomes, decided.Decided, current, rounds);

            current = copy.DecideRound(AlsoRemoveRounds.Opening, proposals);
            rounds.Add(proposals);
            var removed = current.LeftIn(current.Rounds[^1]);
            if (removed.IsEmpty) return Summarize(outcomes, decided.Decided, current, rounds);

            (current, var spreadRounds) = copy.DecideRoundsUntilNothingRemoved(AlsoRemoveRounds.Opening, alsoRemoveRoundsAfter(current), removed);
            rounds.AddRange(spreadRounds.Select(round => round.Proposals));
        }
    }

    /// <remarks>
    /// Without a restored object the target objects stand where the rounds measured them (a removed one is left out by the
    /// world's own test), so a world is built from the decisions only when a round has restored an object since the last one.
    /// </remarks>
    private static StandingTargets StandingAfter(StandingTargets standing, IRemovalDecisions current, DecidedTargets decided)
    {
        var restoredSince = current.Rounds.Skip(standing.RoundsChecked).Any(round => !current.RestoredIn(round).IsEmpty);
        return restoredSince ? PlaceStanding(current, decided) : standing with { RoundsChecked = current.Rounds.Length };
    }

    private static StandingTargets PlaceStanding(IRemovalDecisions current, DecidedTargets decided)
    {
        var targets = current.TargetsAsTheyStand(decided.Targets);
        return new StandingTargets(targets, decided.IndexTargets(targets), current.Rounds.Length);
    }

    /// <summary>The target objects where they stand, and the index of them.</summary>
    /// <param name="RoundsChecked">The number of the decisions' first rounds whose restored objects this world already places at their earlier placement.</param>
    private sealed record StandingTargets(ImmutableArray<TargetObject> Targets, IVisibleTargetObjects Index, int RoundsChecked);

    /// <summary>The other mods' objects left without support once the target objects are as <paramref name="current"/> decides.</summary>
    /// <param name="standing">The target objects where they stand under <paramref name="current"/>.</param>
    /// <param name="before">The weights the judged objects had before any decision, shared by every settling of the rounds.</param>
    private ImmutableArray<OtherModsObjectOutcome> SettleAgainst(
        IRemovalDecisions current,
        StandingTargets standing,
        DecidedTargets decided,
        IVisibleObjectsOfAnyPlugin others,
        OtherModsObjectRules rules,
        WeightsBefore before)
    {
        var settler = SupportJudgingParts.CreateLeftWithoutSupport(
            judging,
            new SupportWorld(decided.Targets, decided.Visible, _ => true, others, SupportWorld.NoChanges, judging.Caches.Shapes, judging.Caches.Terrain),
            new SupportWorld(standing.Targets, standing.Index, target => !current.IsRemoved(target), others, SupportWorld.NoChanges, judging.Caches.Shapes, judging.Caches.Terrain),
            others,
            rules,
            rules.IsJudged,
            before);
        return settler.Settle(FindChangedTargets(decided.Targets, current, rules), SupportWorld.NoChanges);
    }

    private TargetsOnTakenAwayObjects CreateTargetsOnTakenAway(DecidedTargets decided, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules) =>
        new(
            decided.AsMeasured,
            decided.VisibleAsMeasured,
            new SupportWorld(decided.AsMeasured, decided.VisibleAsMeasured, _ => true, others, SupportWorld.NoChanges, judging.Caches.Shapes, judging.Caches.Terrain),
            SupportJudgingParts.CreateSupport(judging),
            rules,
            judging.Caches.Triangles,
            alsoRemove,
            judging.Execution,
            ImmutableDictionary<OtherId, OtherObject>.Empty);

    /// <summary>The objects removed or pulled down, where they stood, in id order.</summary>
    private static List<OtherLeaving> TakenAway(ImmutableArray<OtherModsObjectOutcome> outcomes) =>
        [.. outcomes.Where(outcome => outcome.IsTakenAway).Select(outcome => new OtherLeaving(outcome.Object, To: null))];

    /// <param name="decided">The decisions the rounds started from.</param>
    /// <param name="current">The decisions with the rounds.</param>
    private static OtherModsObjectsResult Summarize(
        ImmutableArray<OtherModsObjectOutcome> outcomes, IRemovalDecisions decided, IRemovalDecisions current, List<IReadOnlyList<ProposedRemoval>> rounds) =>
        new(StepOutcome.Performed, outcomes, current.RoundsAfter(decided), [.. rounds], Failure: null);

    /// <summary>The target objects that could hold up others and were removed, shrunk or turned, where they stood, in target order.</summary>
    private static List<BoxInSpace> FindChangedTargets(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions, OtherModsObjectRules rules) =>
        [.. targets.Where(target => decisions.HasLeftItsPlace(target.Id) || decisions.Of(target.Id) is Decision.Fitted).Select(rules.SupportingBoxOf).OfType<BoxInSpace>()];
}
