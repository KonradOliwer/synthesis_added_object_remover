using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>What the step for other mods' objects reads besides its own inputs.</summary>
/// <param name="Trust">Decides whose objects are judged; the cleaned mod's records a later plugin overrides are not judged.</param>
/// <param name="ObjectsOfAnyPlugin">Asked only when there is something to judge: the first ask builds their index.</param>
internal sealed record OtherModsObjectsContext(
    ModTrustTable Trust,
    LoadOrderPlugins Plugins,
    OtherModsObjectsSettings Settings,
    IBaseObjectShapes Shapes,
    ITriangleMeshes Triangles,
    ISurfaceSamples Surfaces,
    ITerrainHeights Terrain,
    Func<IVisibleObjectsOfAnyPlugin> ObjectsOfAnyPlugin,
    Execution Execution)
{
    public OtherModsObjectRules CreateRules() => new(Trust, Plugins, Shapes);

    public SurfaceContacts CreateSurfaceContacts() => new(Terrain, Triangles, Surfaces, Settings.TouchGap);

    public OtherObjectSupport CreateSupport() => new(CreateSurfaceContacts(), Settings.TouchGap);

    public DropFinder CreateDropFinder() => new(CreateSurfaceContacts(), Triangles, Terrain, Settings.TouchGap);

    public SupportLoss CreateSupportLoss() => new(CreateSupport(), CreateDropFinder(), Settings.SupportLostFraction);

    public JudgedObjectsNear CreateNear(IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules) => new(others, rules, Settings.TouchGap, Execution);

    /// <param name="world">Every target object and object of any plugin where it stands before any decision.</param>
    public TargetsHoldingUpOtherModsObjects CreateHolding(SupportWorld world, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules) =>
        new(world, CreateNear(others, rules), rules, CreateSupportLoss(), Execution);
}

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
internal static class OtherModsObjects
{
    /// <param name="targets">Where they stand before any decision.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    public static HeldUpTargets FindHeldUpTargets(ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, OtherModsObjectsContext context)
    {
        if (context.Settings.Handling != OtherModsObjectsHandling.KeepSupportingObject) return HeldUpTargets.SwitchedOff;

        return Failures.Attempt(
            () => new HeldUpTargets(StepOutcome.Performed, CreateHeldUpTargetsFinder(targets, visibleTargets, context).Find(), Failure: null),
            failure => new HeldUpTargets(
                StepOutcome.StoppedByUnexpectedError, new Dictionary<TargetId, RestingObject>(), PartFailure.OfWholePart(OtherModsObjectsParts.HeldUpTargets, failure)));
    }

    public static OtherModsObjectsResult SettleLeftWithoutSupport(DecidedTargets decided, AlsoRemoveSettings alsoRemove, OtherModsObjectsContext context)
    {
        if (context.Settings.Handling == OtherModsObjectsHandling.KeepSupportingObject) return OtherModsObjectsResult.SwitchedOff;

        return Failures.Attempt(
            () => Settle(decided, alsoRemove, context),
            failure => new OtherModsObjectsResult(StepOutcome.StoppedByUnexpectedError, [], [], [], PartFailure.OfWholePart(OtherModsObjectsParts.LeftWithoutSupport, failure)));
    }

    /// <summary>The fit step's rule: the objects target objects or judged objects of other mods rest on, whatever the setting.</summary>
    /// <param name="targets">Where they stand before any decision.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    /// <param name="heldUp">What <see cref="FindHeldUpTargets"/> found for the same targets.</param>
    /// <remarks>
    /// When the held-up targets were found, the judged objects' holders are not searched again: that search ran the same test
    /// before any decision near every target, so every holder the fit's own search would find is a held-up target, and each
    /// of those is kept, so the fit never checks one.
    /// </remarks>
    public static IRestingRule CreateRestingRule(
        ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, RestingRule targetObjects, HeldUpTargets heldUp, OtherModsObjectsContext context) =>
        new AnyObjectRestingRule(
            targetObjects,
            heldUp.Outcome == StepOutcome.Performed
                ? _ => []
                : checkedObjects => FindHeldUpAmong(targets, visibleTargets, context, checkedObjects));

    private static IEnumerable<TargetId> FindHeldUpAmong(
        ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, OtherModsObjectsContext context, IReadOnlySet<TargetId> checkedObjects) =>
        CreateHolding(targets, visibleTargets, context).Find(checkedObjects.Select(target => targets[target.Index])).Keys.Where(checkedObjects.Contains);

    private static HeldUpTargetsFinder CreateHeldUpTargetsFinder(ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, OtherModsObjectsContext context) =>
        new(targets, CreateHolding(targets, visibleTargets, context), context.Execution);

    private static TargetsHoldingUpOtherModsObjects CreateHolding(ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, OtherModsObjectsContext context)
    {
        var others = context.ObjectsOfAnyPlugin();
        var rules = context.CreateRules();
        return context.CreateHolding(new SupportWorld(targets, visibleTargets, _ => true, others, SupportWorld.NoChanges, context.Shapes), others, rules);
    }

    /// <remarks>Without a changed target object nothing can have lost its support, so the objects of any plugin are not asked for.</remarks>
    private static OtherModsObjectsResult Settle(DecidedTargets decided, AlsoRemoveSettings alsoRemove, OtherModsObjectsContext context)
    {
        var rules = context.CreateRules();
        if (FindChangedTargets(decided.Targets, decided.Decided, rules).Count == 0) return new OtherModsObjectsResult(StepOutcome.Performed, [], [], [], Failure: null);

        var others = context.ObjectsOfAnyPlugin();
        var before = new WeightsBefore([]);
        var copy = decided.Decided.WorkingCopy();
        var current = decided.Decided;
        var standing = new StandingTargets(decided.AsMeasured, decided.VisibleAsMeasured, RoundsChecked: 0);
        var rounds = new List<IReadOnlyList<ProposedRemoval>>();
        while (true)
        {
            standing = StandingAfter(standing, current, decided);
            var outcomes = SettleAgainst(current, standing, decided, others, rules, before, context);
            var proposals = alsoRemove.Mode == FollowUpRemovalMode.Nothing
                ? []
                : CreateTargetsOnTakenAway(decided, others, rules, alsoRemove, context).Propose(current, TakenAway(outcomes));
            if (proposals.Count == 0) return Summarize(outcomes, decided.Decided, current, rounds);

            current = copy.DecideRound(RoundKind.AlsoRemove, proposals);
            rounds.Add(proposals);
            var removed = current.LeftIn(current.Rounds[^1]);
            if (removed.IsEmpty) return Summarize(outcomes, decided.Decided, current, rounds);

            var spread = RestingObjects.PrepareFrom(current, CreateRoundsInput(decided, others, context), alsoRemove);
            (current, var spreadRounds) = copy.DecideRoundsUntilNothingRemoved(spread.Rounds, removed);
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
        OtherModsObjectsContext context)
    {
        var settler = new LeftWithoutSupport(
            new SupportWorld(decided.Targets, decided.Visible, _ => true, others, SupportWorld.NoChanges, context.Shapes),
            new SupportWorld(standing.Targets, standing.Index, target => !current.IsRemoved(target), others, SupportWorld.NoChanges, context.Shapes),
            context.CreateNear(others, rules),
            rules,
            context.CreateSupportLoss(),
            context.CreateDropFinder(),
            context.Settings,
            before,
            context.Execution);
        return settler.Settle(FindChangedTargets(decided.Targets, current, rules));
    }

    private static TargetsOnTakenAwayObjects CreateTargetsOnTakenAway(
        DecidedTargets decided, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules, AlsoRemoveSettings alsoRemove, OtherModsObjectsContext context) =>
        new(
            decided.AsMeasured,
            decided.VisibleAsMeasured,
            new SupportWorld(decided.AsMeasured, decided.VisibleAsMeasured, _ => true, others, SupportWorld.NoChanges, context.Shapes),
            context.CreateSupport(),
            rules,
            context.Triangles,
            alsoRemove,
            context.Execution);

    private static RoundsInput CreateRoundsInput(DecidedTargets decided, IVisibleObjectsOfAnyPlugin others, OtherModsObjectsContext context) =>
        new(decided.AsMeasured, others,context.Shapes, context.Triangles, context.Surfaces, context.Terrain, context.Execution);

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
