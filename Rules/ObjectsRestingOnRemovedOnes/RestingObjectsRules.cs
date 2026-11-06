using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// Creates the rules of objects resting on removed ones and of other mods' objects left without support. The also-remove rounds,
/// seeded with removals, remove what goes with them, as the also-remove mode says, until a round removes nothing. Other mods'
/// objects resting on target objects are judged by the support relation whatever the also-remove mode is: that mode governs only
/// target objects. With KeepSupportingObject the target objects they rest on are found before any decision, to be kept
/// (<see cref="HeldUpTargetsFinder"/>); otherwise, once the also-remove rounds are decided, the other mods' objects left without
/// support are removed or pulled down (<see cref="LeftWithoutSupportRounds"/>). Each part stops on its own on an unexpected
/// failure. Whatever the setting, no target object that a judged object rests on counts as free to shrink or turn
/// (<see cref="AnyObjectRestingRule"/>).
/// </summary>
public static class RestingObjectsRules
{
    /// <summary>How many points the support rule samples on each mesh's surface, which the surface sample cache spreads.</summary>
    public static int SurfaceSamplesPerMesh() => SurfaceContacts.SamplesPerMesh;

    /// <summary>The also-remove rounds seeded by given rounds (see <see cref="RestingObjectsFinder"/>).</summary>
    /// <param name="timeSinceStart">The run's clock, which times the rounds alone.</param>
    /// <param name="reportFiles">Whether the report files are written, which decides whether their details are found.</param>
    /// <param name="caches">
    /// Its base facts, shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when the
    /// support rounds have seeds: the first ask builds the supporters' bounds index.
    /// </param>
    public static IRestingObjectsRounds CreateRounds(
        AlsoRemoveSettings settings, Execution execution, Func<TimeSpan> timeSinceStart, StepOutcome reportFiles, IRunCaches caches) =>
        new RestingObjectsRounds(settings, execution, timeSinceStart, reportFiles, caches);

    /// <summary>
    /// The also-remove rounds that follow up removals pass by pass (see <see cref="IAlsoRemoveFollowUp"/>); its result
    /// computes no report-file details.
    /// </summary>
    /// <param name="targets">Where they stood before any decision.</param>
    /// <param name="decisions">The decisions the rounds start from; the rounds measure the target objects where these place them.</param>
    /// <param name="timeSinceStart">The run's clock, which measures the rounds.</param>
    /// <param name="caches">
    /// Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when the support rounds
    /// have seeds: the first ask builds the supporters' bounds index.
    /// </param>
    public static IAlsoRemoveFollowUp CreateFollowUp(
        ImmutableArray<TargetObject> targets,
        IRemovalDecisions decisions,
        AlsoRemoveSettings settings,
        Execution execution,
        Func<TimeSpan> timeSinceStart,
        IRunCaches caches) =>
        new AlsoRemoveFollowUp(new FollowUpInput(targets, decisions, settings, execution, timeSinceStart, caches));

    /// <summary>The objects other target objects rest on, before any removal (see <see cref="RestingRule"/>).</summary>
    /// <param name="targets">Where they stand before any object is shrunk or turned.</param>
    /// <param name="drops">Asks the drop test about the same targets where they stand.</param>
    /// <param name="caches">Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when there is an object to check.</param>
    public static IRestingRule CreateTargetObjectsRestingRule(
        ImmutableArray<TargetObject> targets, AlsoRemoveSettings settings, ITargetObjectsDropTest drops, Execution execution, IRunCaches caches) =>
        new RestingRule(new RestingRuleInput(targets, settings, drops, execution, caches));

    /// <summary>The also-remove rounds decided on a working copy of the decisions, spreading from removals made there (see <see cref="AlsoRemoveRoundsAfter"/>).</summary>
    /// <param name="caches">Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked each time the rounds are created.</param>
    public static IAlsoRemoveRoundsAfter CreateAlsoRemoveRoundsAfter(AlsoRemoveSettings settings, Execution execution, IRunCaches caches) =>
        new AlsoRemoveRoundsAfter(new RoundsAfterInput(settings, execution, caches));

    /// <summary>Measures the contacts that support an object (see <see cref="SurfaceContacts"/>).</summary>
    /// <param name="caches">Its triangles, surface samples and terrain are read.</param>
    public static ISupportContacts CreateSupportContacts(ILoadOrderCaches caches) => new SurfaceContacts(caches.Triangles, caches.SurfaceSamples);

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
    /// <param name="targetObjects">The objects other target objects rest on.</param>
    /// <param name="heldUp">What the search of <see cref="CreateHeldUpTargetsSearch"/> found for the same targets.</param>
    public static IRestingRule CreateAnyObjectRestingRule(SupportJudging judging, IRestingRule targetObjects, HeldUpTargets heldUp) =>
        new AnyObjectRestingRule(
            targetObjects,
            heldUp.Outcome == StepOutcome.Performed
                ? _ => []
                : checkedObjects => FindHeldUpAmong(judging, checkedObjects));

    /// <summary>
    /// Whether a target object holds up another, by the drop test that finds the other mods' objects left without support, in the
    /// world before any decision.
    /// </summary>
    /// <param name="alsoRemove">The settings whose gap counts as touching.</param>
    public static ITargetObjectsDropTest CreateDropTest(SupportJudging judging, AlsoRemoveSettings alsoRemove) =>
        new TargetObjectsDropTest(
            judging.Targets,
            judging.Caches.VisibleTargets,
            SupportJudgingParts.CreateRules(judging),
            SupportJudgingParts.CreateDropFinder(judging, alsoRemove.TouchGap),
            judging.Caches.Shapes,
            judging.Caches.Terrain,
            judging.Caches.VisibleObjectsOfAnyPlugin,
            judging.Execution);

    /// <summary>The follow-up of objects leaving outside the removal rounds (see <see cref="FollowUpOfObjectsLeaving"/>).</summary>
    public static IFollowUpOfObjectsLeaving CreateFollowUpOfObjectsLeaving(SupportJudging judging, AlsoRemoveSettings alsoRemove, IAlsoRemoveRoundsAfter alsoRemoveRounds) =>
        new FollowUpOfObjectsLeaving(judging, alsoRemove, alsoRemoveRounds);

    /// <summary>
    /// RemoveFloatingObject and PullObjectToTheGround: what becomes of the other mods' objects left without support (see
    /// <see cref="LeftWithoutSupportRounds"/>).
    /// </summary>
    /// <param name="alsoRemoveRounds">Asked only when <paramref name="alsoRemove"/> is not Nothing.</param>
    public static ILeftWithoutSupportRounds CreateLeftWithoutSupportRounds(SupportJudging judging, AlsoRemoveSettings alsoRemove, IAlsoRemoveRoundsAfter alsoRemoveRounds) =>
        new LeftWithoutSupportRounds(judging, alsoRemove, alsoRemoveRounds);

    private static IEnumerable<TargetId> FindHeldUpAmong(SupportJudging judging, IReadOnlySet<TargetId> checkedObjects) =>
        SupportJudgingParts.CreateHoldingBeforeAnyDecision(judging)
            .Find(checkedObjects.Select(target => judging.Targets[target.Index]), isLeftOut: _ => false).Keys.Where(checkedObjects.Contains);
}
