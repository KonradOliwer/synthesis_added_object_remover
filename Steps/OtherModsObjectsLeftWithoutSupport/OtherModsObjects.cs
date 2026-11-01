using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

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

    public JudgedObjectsNear CreateNear(IVisibleObjectsOfAnyPlugin others, Func<OtherObject, bool> isJudged) => new(others, isJudged, Settings.TouchGap, Execution);

    /// <param name="world">The target objects and objects of any plugin the support is judged among.</param>
    /// <param name="isJudged">Which objects of <paramref name="others"/> are checked for resting on a target object.</param>
    public TargetsHoldingUpOtherModsObjects CreateHolding(SupportWorld world, IVisibleObjectsOfAnyPlugin others, OtherModsObjectRules rules, Func<OtherObject, bool> isJudged) =>
        new(world, CreateNear(others, isJudged), rules, CreateSupportLoss(), Execution);
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
public static class OtherModsObjects
{
    /// <summary>
    /// KeepSupportingObject: the target objects other mods' objects rest on, directly or through other target objects, each where
    /// it stands before any decision; switched off otherwise.
    /// </summary>
    /// <param name="targets">Where they stand before any decision.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    /// <param name="trust">Decides whose objects are judged.</param>
    /// <param name="objectsOfAnyPlugin">Asked only with KeepSupportingObject.</param>
    public static HeldUpTargets FindHeldUpTargets(
        ImmutableArray<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        ModTrustTable trust,
        LoadOrderPlugins plugins,
        OtherModsObjectsSettings settings,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
        Execution execution)
    {
        if (settings.Handling != OtherModsObjectsHandling.KeepSupportingObject) return HeldUpTargets.SwitchedOff;

        var context = new OtherModsObjectsContext(trust, plugins, settings, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin, execution);
        return Failures.Attempt(
            () => new HeldUpTargets(StepOutcome.Performed, CreateHeldUpTargetsFinder(targets, visibleTargets, context).Find(), Failure: null),
            failure => new HeldUpTargets(
                StepOutcome.StoppedByUnexpectedError, new Dictionary<TargetId, RestingObject>(), PartFailure.OfWholePart(OtherModsObjectsParts.HeldUpTargets, failure)));
    }

    /// <summary>
    /// The fit step's question asked before any removal: the objects <paramref name="targetObjects"/> finds, and those judged objects
    /// of other mods rest on, whatever <paramref name="settings"/> says (see <see cref="AnyObjectRestingRule"/>).
    /// </summary>
    /// <param name="targetObjects">The objects other target objects rest on.</param>
    /// <param name="targets">Where they stand before any object is shrunk or turned.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    /// <param name="trust">Decides whose objects are judged.</param>
    /// <param name="objectsOfAnyPlugin">Asked only when there is an object to check.</param>
    /// <param name="heldUp">What <see cref="FindHeldUpTargets"/> found for the same targets.</param>
    /// <remarks>
    /// When the held-up targets were found, the judged objects' holders are not searched again: that search ran the same test
    /// before any decision near every target, so every holder the fit's own search would find is a held-up target, and each
    /// of those is kept, so the fit never checks one.
    /// </remarks>
    public static IRestingRule CreateRestingRule(
        IRestingRule targetObjects,
        ImmutableArray<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        ModTrustTable trust,
        LoadOrderPlugins plugins,
        OtherModsObjectsSettings settings,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
        HeldUpTargets heldUp,
        Execution execution)
    {
        var context = new OtherModsObjectsContext(trust, plugins, settings, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin, execution);
        return new AnyObjectRestingRule(
            targetObjects,
            heldUp.Outcome == StepOutcome.Performed
                ? _ => []
                : checkedObjects => FindHeldUpAmong(targets, visibleTargets, context, checkedObjects));
    }

    /// <summary>
    /// Whether a target object holds up another, by the drop test that finds the other mods' objects left without support, in the
    /// world before any decision.
    /// </summary>
    /// <param name="targets">Where they stand before any decision.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    /// <param name="alsoRemove">The settings of the rule that asks, whose gap counts as touching.</param>
    /// <param name="objectsOfAnyPlugin">Asked only when there is a pair to test.</param>
    public static ITargetObjectsDropTest CreateDropTest(
        ImmutableArray<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        ModTrustTable trust,
        LoadOrderPlugins plugins,
        AlsoRemoveSettings alsoRemove,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
        Execution execution) =>
        new TargetObjectsDropTest(
            targets,
            visibleTargets,
            new OtherModsObjectRules(trust, plugins, shapes),
            new DropFinder(new SurfaceContacts(terrain, triangles, surfaces, alsoRemove.TouchGap), triangles, terrain, alsoRemove.TouchGap),
            shapes,
            objectsOfAnyPlugin,
            execution);

    /// <summary>
    /// The objects that move away from where they stand now and that an object that stays rests on there, by the test that finds the
    /// other mods' objects left without support. Every object of <paramref name="staying"/> that can rest counts, whichever plugin it
    /// belongs to.
    /// </summary>
    /// <param name="holders">Where they stand now; the id of each is its position.</param>
    /// <param name="holderIndex">The index of <paramref name="holders"/>.</param>
    /// <param name="movesAway">Which holders move away; the others stay, as their copies in <paramref name="staying"/>.</param>
    /// <param name="staying">The visible objects of any plugin as they stand once the run is done, with a copy of each holder.</param>
    /// <param name="copiesOfMoving">The copies in <paramref name="staying"/> of the holders that move away, which neither rest nor hold up.</param>
    /// <returns>Each holder that moves away and holds up an object that stays, with the first such object in id order.</returns>
    public static IReadOnlyDictionary<TargetId, OtherObject> FindHoldersOfObjectsThatStay(
        IReadOnlyList<TargetObject> holders,
        IVisibleTargetObjects holderIndex,
        Func<TargetId, bool> movesAway,
        IVisibleObjectsOfAnyPlugin staying,
        IReadOnlySet<OtherId> copiesOfMoving,
        ModTrustTable trust,
        LoadOrderPlugins plugins,
        OtherModsObjectsSettings settings,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Execution execution)
    {
        var context = new OtherModsObjectsContext(trust, plugins, settings, shapes, triangles, surfaces, terrain, () => staying, execution);
        var world = new SupportWorld(holders, holderIndex, movesAway, staying, copiesOfMoving.ToDictionary(id => id, _ => (PlacedTransform?)null), shapes);
        var rules = context.CreateRules();
        return context.CreateHolding(world, staying, rules, rules.CanRest).Find(holders.Where(holder => movesAway(holder.Id)), copiesOfMoving.Contains);
    }

    /// <summary>
    /// RemoveFloatingObject and PullObjectToTheGround: what becomes of the other mods' objects left without support once the
    /// also-remove rounds are decided, and the also-remove rounds for the target objects resting on or touching those taken away;
    /// switched off with KeepSupportingObject. The rounds are decided on a working copy of <paramref name="decided"/>, which never
    /// changes, and proposed for the runner to apply.
    /// </summary>
    /// <param name="targets">Where they stood before any decision.</param>
    /// <param name="targetsAsMeasured">Each target object kept shrunk or turned at its new placement, as the rounds measure it.</param>
    /// <param name="decided">The decisions of every removal round so far, the also-remove rounds included.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    /// <param name="visibleTargetsAsMeasured">The index of <paramref name="targetsAsMeasured"/>.</param>
    /// <param name="indexTargets">The index of the given target objects.</param>
    /// <param name="trust">Decides whose objects are judged.</param>
    /// <param name="alsoRemoveRoundsAfter">
    /// The also-remove rounds that spread from removals made in the given decisions, measuring the target objects as
    /// <paramref name="targetsAsMeasured"/>; asked only when <paramref name="alsoRemove"/> is not Nothing.
    /// </param>
    /// <param name="objectsOfAnyPlugin">Asked only when a target object was removed, shrunk or turned.</param>
    public static OtherModsObjectsResult SettleLeftWithoutSupport(
        ImmutableArray<TargetObject> targets,
        ImmutableArray<TargetObject> targetsAsMeasured,
        IRemovalDecisions decided,
        IVisibleTargetObjects visibleTargets,
        IVisibleTargetObjects visibleTargetsAsMeasured,
        Func<ImmutableArray<TargetObject>, IVisibleTargetObjects> indexTargets,
        ModTrustTable trust,
        LoadOrderPlugins plugins,
        OtherModsObjectsSettings settings,
        AlsoRemoveSettings alsoRemove,
        Func<IRemovalDecisions, IRemovalRounds<RoundDetails>> alsoRemoveRoundsAfter,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
        Execution execution)
    {
        if (settings.Handling == OtherModsObjectsHandling.KeepSupportingObject) return OtherModsObjectsResult.SwitchedOff;

        var targetsDecided = new DecidedTargets(targets, targetsAsMeasured, decided, visibleTargets, visibleTargetsAsMeasured, indexTargets);
        var context = new OtherModsObjectsContext(trust, plugins, settings, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin, execution);
        return Failures.Attempt(
            () => Settle(targetsDecided, alsoRemove, alsoRemoveRoundsAfter, context),
            failure => new OtherModsObjectsResult(StepOutcome.StoppedByUnexpectedError, [], [], [], PartFailure.OfWholePart(OtherModsObjectsParts.LeftWithoutSupport, failure)));
    }

    private static IEnumerable<TargetId> FindHeldUpAmong(
        ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, OtherModsObjectsContext context, IReadOnlySet<TargetId> checkedObjects) =>
        CreateHolding(targets, visibleTargets, context).Find(checkedObjects.Select(target => targets[target.Index]), isLeftOut: _ => false).Keys.Where(checkedObjects.Contains);

    private static HeldUpTargetsFinder CreateHeldUpTargetsFinder(ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, OtherModsObjectsContext context) =>
        new(targets, CreateHolding(targets, visibleTargets, context), context.Execution);

    private static TargetsHoldingUpOtherModsObjects CreateHolding(ImmutableArray<TargetObject> targets, IVisibleTargetObjects visibleTargets, OtherModsObjectsContext context)
    {
        var others = context.ObjectsOfAnyPlugin();
        var rules = context.CreateRules();
        return context.CreateHolding(new SupportWorld(targets, visibleTargets, _ => true, others, SupportWorld.NoChanges, context.Shapes), others, rules, rules.IsJudged);
    }

    /// <remarks>Without a changed target object nothing can have lost its support, so the objects of any plugin are not asked for.</remarks>
    private static OtherModsObjectsResult Settle(
        DecidedTargets decided,
        AlsoRemoveSettings alsoRemove,
        Func<IRemovalDecisions, IRemovalRounds<RoundDetails>> alsoRemoveRoundsAfter,
        OtherModsObjectsContext context)
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

            (current, var spreadRounds) = copy.DecideRoundsUntilNothingRemoved(alsoRemoveRoundsAfter(current), removed);
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
            context.CreateNear(others, rules.IsJudged),
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
