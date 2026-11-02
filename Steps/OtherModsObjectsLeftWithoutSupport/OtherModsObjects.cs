using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

/// <summary>The target objects as the also-remove step left them.</summary>
/// <param name="Targets">Where they stood before any decision.</param>
/// <param name="AsMeasured">Each target object shrunk or turned at its new placement, also one removed later, as the rounds measured it.</param>
/// <param name="Decided">The decisions of every removal round so far.</param>
/// <param name="Visible">The index of <paramref name="Targets"/>.</param>
/// <param name="VisibleAsMeasured">The index of <paramref name="AsMeasured"/>.</param>
/// <param name="IndexTargets">The index of the given target objects.</param>
internal sealed record DecidedTargets(
    ImmutableArray<TargetObject> Targets,
    ImmutableArray<TargetObject> AsMeasured,
    IRemovalDecisions Decided,
    IVisibleTargetObjects Visible,
    IVisibleTargetObjects VisibleAsMeasured,
    Func<ImmutableArray<TargetObject>, IVisibleTargetObjects> IndexTargets);

/// <summary>
/// Other mods' objects resting on target objects, judged by the support relation whatever the also-remove mode is: that mode
/// governs only target objects. With KeepSupportingObject the target objects they rest on are found before any decision, to be
/// kept (<see cref="HeldUpTargetsFinder"/>); otherwise, once the also-remove rounds are decided, the other mods' objects left
/// without support are removed or pulled down (<see cref="LeftWithoutSupport"/>). A target object resting on or touching one taken
/// away goes as the also-remove mode says for any removed support (<see cref="TargetsOnTakenAwayObjects"/>), then what goes with
/// it, and the other mods' objects are judged again against the new decisions, until no further target object is removed: each
/// pass removes one at least, so it ends. Each part stops on its own on an unexpected failure. Whatever the setting, the fit
/// step never shrinks or turns a target object that a judged object rests on (<see cref="AnyObjectRestingRule"/>).
/// </summary>
public static class OtherModsObjects
{
    /// <summary>What every part of the step reads, and every rule it provides to other steps.</summary>
    /// <param name="Targets">Where they stand before any decision.</param>
    /// <param name="Caches">
    /// Index the target objects and give the shapes, triangles and terrain; their objects of any plugin are asked only when there is
    /// something to judge: the first ask builds their index. The search for the holders of objects that stay asks the caches of its
    /// worlds instead.
    /// </param>
    /// <param name="Trust">Decides whose objects are judged; the cleaned mod's records a later plugin overrides are not judged.</param>
    /// <param name="Contacts">The support rule the support of an object is measured with.</param>
    public sealed record SupportJudging(
        ImmutableArray<TargetObject> Targets,
        IRunCaches Caches,
        ModTrustTable Trust,
        LoadOrderPlugins Plugins,
        OtherModsObjectsSettings Settings,
        ISupportContacts Contacts,
        Execution Execution)
    {
        internal OtherModsObjectRules CreateRules() => new(Trust, Plugins, Caches.Shapes);

        internal OtherObjectSupport CreateSupport() => new(Contacts, Settings.TouchGap);

        /// <param name="touchGap">The largest gap that counts as touching for the rule that asks.</param>
        internal DropFinder CreateDropFinder(float touchGap) => new(Contacts, Caches.Triangles, Caches.Terrain, touchGap);

        internal SupportLoss CreateSupportLoss() => new(CreateSupport(), CreateDropFinder(Settings.TouchGap), Settings.SupportLostFraction);

        internal JudgedObjectsNear CreateNear(IVisibleObjectsOfAnyPlugin others, Func<OtherObject, bool> isJudged) => new(others, isJudged, Settings.TouchGap, Execution);

        /// <param name="world">The target objects and objects of any plugin the support is judged among.</param>
        /// <param name="isJudged">Which objects of <paramref name="others"/> are checked for resting on a target object.</param>
        internal TargetsHoldingUpOtherModsObjects CreateHolding(SupportWorld world, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules, Func<OtherObject, bool> isJudged) =>
            new(world, CreateNear(others, isJudged), rules, CreateSupportLoss(), Execution);

        /// <summary>The search among every target object and object of any plugin where it stands before any decision.</summary>
        internal TargetsHoldingUpOtherModsObjects CreateHoldingBeforeAnyDecision()
        {
            var others = Caches.VisibleObjectsOfAnyPlugin();
            var rules = CreateRules();
            return CreateHolding(new SupportWorld(Targets, Caches.VisibleTargets, _ => true, others, SupportWorld.NoChanges, Caches.Shapes), others, rules, rules.IsJudged);
        }
    }

    /// <param name="AfterFits">The decisions with every fit: the rounds measure an object shrunk or turned and removed later at its new placement.</param>
    /// <param name="Decisions">The decisions of every removal round so far, the also-remove rounds included; the step's rounds are added to them.</param>
    /// <param name="AlsoRemoveRounds">Asked only when <paramref name="AlsoRemove"/> is not Nothing.</param>
    public sealed record Input(
        SupportJudging Judging, IRemovalDecisions AfterFits, IRunDecisions Decisions, AlsoRemoveSettings AlsoRemove, IAlsoRemoveRoundsAfter AlsoRemoveRounds);

    /// <summary>
    /// RemoveFloatingObject and PullObjectToTheGround: what becomes of the other mods' objects left without support (see
    /// <see cref="SettleLeftWithoutSupport"/>), with the step's also-remove rounds added to the run's decisions.
    /// </summary>
    public static Decided<OtherModsObjectsResult> Settle(Input input)
    {
        var result = SettleLeftWithoutSupport(input);
        return new Decided<OtherModsObjectsResult>(result, input.Decisions.ApplyRounds(AlsoRemoveRounds.Opening, result.ProposedRounds));
    }

    /// <summary>
    /// What becomes of the other mods' objects left without support once the also-remove rounds are decided, and the also-remove
    /// rounds for the target objects resting on or touching those taken away; switched off with KeepSupportingObject. The rounds
    /// are decided on a working copy of the decisions, which never changes, and proposed. The rounds measure each target object
    /// kept shrunk or turned at its new placement.
    /// </summary>
    /// <remarks>The objects of any plugin are asked only when a target object was removed, shrunk or turned.</remarks>
    private static OtherModsObjectsResult SettleLeftWithoutSupport(Input input)
    {
        var judging = input.Judging;
        if (judging.Settings.Handling == OtherModsObjectsHandling.KeepSupportingObject) return OtherModsObjectsResult.SwitchedOff;

        var targetsAsMeasured = input.AfterFits.TargetsAsTheRoundsMeasureThem(judging.Targets);
        var visibleTargetsAsMeasured = judging.Caches.IndexVisibleTargets(targetsAsMeasured);
        var targetsDecided = new DecidedTargets(
            judging.Targets, targetsAsMeasured, input.Decisions, judging.Caches.VisibleTargets, visibleTargetsAsMeasured, placed => judging.Caches.IndexVisibleTargets(placed));
        return Failures.Attempt(
            () => Settle(targetsDecided, input.AlsoRemove, decided => input.AlsoRemoveRounds.After(decided, targetsAsMeasured), judging),
            failure => new OtherModsObjectsResult(StepOutcome.StoppedByUnexpectedError, [], [], [], PartFailure.OfWholePart(OtherModsObjectsParts.LeftWithoutSupport, failure)));
    }

    /// <param name="TargetObjects">The objects other target objects rest on.</param>
    /// <param name="HeldUp">What the search of <see cref="CreateHeldUpTargetsSearch"/> found for the same targets.</param>
    public sealed record RestingRuleInput(SupportJudging Judging, IRestingRule TargetObjects, HeldUpTargets HeldUp);

    /// <param name="AlsoRemove">The settings whose gap counts as touching.</param>
    public sealed record DropTestInput(SupportJudging Judging, AlsoRemoveSettings AlsoRemove);

    /// <param name="WorldCaches">The object caches of the worlds asked about.</param>
    public sealed record HoldersInput(SupportJudging Judging, IObjectCachesOfWorlds WorldCaches);

    /// <summary>Finds the target objects that hold up other mods' objects, in the world before any decision (see <see cref="HeldUpTargetsSearch"/>).</summary>
    public static IHeldUpTargetsSearch CreateHeldUpTargetsSearch(SupportJudging judging) => new HeldUpTargetsSearch(judging);

    /// <summary>
    /// The objects resting on checked objects, before any removal: those the target objects' rule finds, and those judged objects
    /// of other mods rest on, whatever the settings say (see <see cref="AnyObjectRestingRule"/>).
    /// </summary>
    /// <remarks>
    /// When the held-up targets were found, the judged objects' holders are not searched again: that search ran the same test
    /// before any decision near every target, so every holder this rule's own search would find is a held-up target, and each
    /// of those is kept, so it is never checked.
    /// </remarks>
    public static IRestingRule CreateRestingRule(RestingRuleInput input) =>
        new AnyObjectRestingRule(
            input.TargetObjects,
            input.HeldUp.Outcome == StepOutcome.Performed
                ? _ => []
                : checkedObjects => FindHeldUpAmong(input.Judging, checkedObjects));

    /// <summary>
    /// Whether a target object holds up another, by the drop test that finds the other mods' objects left without support, in the
    /// world before any decision.
    /// </summary>
    public static ITargetObjectsDropTest CreateDropTest(DropTestInput input)
    {
        var judging = input.Judging;
        return new TargetObjectsDropTest(
            judging.Targets,
            judging.Caches.VisibleTargets,
            judging.CreateRules(),
            judging.CreateDropFinder(input.AlsoRemove.TouchGap),
            judging.Caches.Shapes,
            judging.Caches.VisibleObjectsOfAnyPlugin,
            judging.Execution);
    }

    /// <summary>Finds, in worlds of what stays, the objects moving away that an object that stays rests on (see <see cref="HoldersOfObjectsThatStay"/>).</summary>
    public static IHoldersOfObjectsThatStay CreateHoldersOfObjectsThatStay(HoldersInput input) => new HoldersOfObjectsThatStay(input.WorldCaches, input.Judging);

    private static IEnumerable<TargetId> FindHeldUpAmong(SupportJudging judging, IReadOnlySet<TargetId> checkedObjects) =>
        judging.CreateHoldingBeforeAnyDecision().Find(checkedObjects.Select(target => judging.Targets[target.Index]), isLeftOut: _ => false).Keys.Where(checkedObjects.Contains);

    /// <remarks>Without a changed target object nothing can have lost its support, so the objects of any plugin are not asked for.</remarks>
    private static OtherModsObjectsResult Settle(
        DecidedTargets decided,
        AlsoRemoveSettings alsoRemove,
        Func<IRemovalDecisions, IRemovalRounds<RoundDetails>> alsoRemoveRoundsAfter,
        SupportJudging judging)
    {
        var rules = judging.CreateRules();
        if (FindChangedTargets(decided.Targets, decided.Decided, rules).Count == 0) return new OtherModsObjectsResult(StepOutcome.Performed, [], [], [], Failure: null);

        var others = judging.Caches.VisibleObjectsOfAnyPlugin();
        var before = new WeightsBefore([]);
        var copy = decided.Decided.WorkingCopy();
        var current = decided.Decided;
        var standing = new StandingTargets(decided.AsMeasured, decided.VisibleAsMeasured, RoundsChecked: 0);
        var rounds = new List<IReadOnlyList<ProposedRemoval>>();
        while (true)
        {
            standing = StandingAfter(standing, current, decided);
            var outcomes = SettleAgainst(current, standing, decided, others, rules, before, judging);
            var proposals = alsoRemove.Mode == FollowUpRemovalMode.Nothing
                ? []
                : CreateTargetsOnTakenAway(decided, others, rules, alsoRemove, judging).Propose(current, TakenAway(outcomes));
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
    /// <param name="before">The weights the judged objects had before any decision, shared by every settling of the step.</param>
    private static ImmutableArray<OtherModsObjectOutcome> SettleAgainst(
        IRemovalDecisions current,
        StandingTargets standing,
        DecidedTargets decided,
        IVisibleObjectsOfAnyPlugin others,
        OtherModsObjectRules rules,
        WeightsBefore before,
        SupportJudging judging)
    {
        var settler = new LeftWithoutSupport(
            new SupportWorld(decided.Targets, decided.Visible, _ => true, others, SupportWorld.NoChanges, judging.Caches.Shapes),
            new SupportWorld(standing.Targets, standing.Index, target => !current.IsRemoved(target), others, SupportWorld.NoChanges, judging.Caches.Shapes),
            judging.CreateNear(others, rules.IsJudged),
            rules,
            judging.CreateSupportLoss(),
            judging.CreateDropFinder(judging.Settings.TouchGap),
            judging.Settings,
            before,
            judging.Execution);
        return settler.Settle(FindChangedTargets(decided.Targets, current, rules));
    }

    private static TargetsOnTakenAwayObjects CreateTargetsOnTakenAway(
        DecidedTargets decided, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules, AlsoRemoveSettings alsoRemove, SupportJudging judging) =>
        new(
            decided.AsMeasured,
            decided.VisibleAsMeasured,
            new SupportWorld(decided.AsMeasured, decided.VisibleAsMeasured, _ => true, others, SupportWorld.NoChanges, judging.Caches.Shapes),
            judging.CreateSupport(),
            rules,
            judging.Caches.Triangles,
            alsoRemove,
            judging.Execution);

    /// <summary>The objects removed or pulled down, where they stood, in id order.</summary>
    private static List<OtherObject> TakenAway(ImmutableArray<OtherModsObjectOutcome> outcomes) =>
        [.. outcomes.Where(outcome => outcome.IsTakenAway).Select(outcome => outcome.Object)];

    /// <param name="decided">The decisions the step started from.</param>
    /// <param name="current">The decisions with the step's rounds.</param>
    private static OtherModsObjectsResult Summarize(
        ImmutableArray<OtherModsObjectOutcome> outcomes, IRemovalDecisions decided, IRemovalDecisions current, List<IReadOnlyList<ProposedRemoval>> rounds) =>
        new(StepOutcome.Performed, outcomes, current.RoundsAfter(decided), [.. rounds], Failure: null);

    /// <summary>The target objects that could hold up others and were removed, shrunk or turned, where they stood, in target order.</summary>
    private static List<BoxInSpace> FindChangedTargets(ImmutableArray<TargetObject> targets, IRemovalDecisions decisions, OtherModsObjectRules rules) =>
        [.. targets.Where(target => decisions.HasLeftItsPlace(target.Id) || decisions.Of(target.Id) is Decision.Fitted).Select(rules.SupportingBoxOf).OfType<BoxInSpace>()];
}
