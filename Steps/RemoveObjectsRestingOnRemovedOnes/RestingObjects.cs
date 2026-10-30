using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove step: seeded with the removals of the rounds the run names as its seed rounds, its rounds remove what
/// goes with them, as the also-remove mode says, until a round removes nothing. It decides the rounds on its own copy
/// of the decisions and proposes them as one proposal, which the runner applies to the run's decisions. It also judges the
/// other mods' objects resting on target objects, and the target objects resting on those (<see cref="OtherModsObjects"/>).
/// </summary>
public static class RestingObjects
{
    /// <summary>How many points the surface samples (C15) spread over each mesh.</summary>
    public static int SurfaceSamplesPerMesh => WeightedSurfaceSamples.SamplesPerMesh;

    /// <summary>
    /// The rounds, then the touch chains when they are shown, then the report-file details when report files are
    /// written, each stopped on its own by an unexpected failure.
    /// </summary>
    /// <param name="objectsOfAnyPlugin">Asked only when the support rounds have seeds: the first ask builds the supporters' bounds index.</param>
    public static RestingObjectsResult Find(
        RestingObjectsInput input,
        IBaseFacts bases,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin)
    {
        if (input.Settings.Mode == FollowUpRemovalMode.Nothing) return NotPerformed(input, StepOutcome.SwitchedOff, failure: null).Result;

        var rounds = Failures.Attempt(
            () => DecideRounds(input, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin),
            failure => NotPerformed(input, StepOutcome.StoppedByUnexpectedError, PartFailure.OfWholePart(RestingObjectsParts.AlsoRemove, failure)));
        var run = rounds with { Result = WithTouchChains(rounds, input) };
        return run.Result with { ReportFileDetails = FindReportFileDetails(run, input, bases, shapes, triangles) };
    }

    /// <summary>
    /// The also-remove rounds that follow up a later step's removals pass by pass (see <see cref="IAlsoRemoveFollowUp"/>); its result
    /// computes no report-file details.
    /// </summary>
    /// <param name="targets">Where the rounds measure them, from the decisions the first pass starts from.</param>
    /// <param name="timeSinceStart">The run's clock, which measures the rounds.</param>
    /// <param name="objectsOfAnyPlugin">Asked only when the support rounds have seeds: the first ask builds the supporters' bounds index.</param>
    public static IAlsoRemoveFollowUp CreateFollowUp(
        ImmutableArray<TargetObject> targets,
        AlsoRemoveSettings settings,
        Execution execution,
        Func<TimeSpan> timeSinceStart,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin) =>
        new AlsoRemoveFollowUp(targets, settings, execution, timeSinceStart, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin);

    /// <summary>
    /// The fit step's question asked before any removal: the objects other target objects rest on (see <see cref="RestingRule"/>), and
    /// those judged objects of other mods rest on, whatever <paramref name="otherModsObjects"/> says (see <see cref="AnyObjectRestingRule"/>).
    /// </summary>
    /// <param name="targets">Where they stand before any object is shrunk or turned.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    /// <param name="trust">Decides whose objects are judged.</param>
    /// <param name="objectsOfAnyPlugin">Asked only when there is an object to check.</param>
    /// <param name="heldUp">What <see cref="FindHeldUpTargets"/> found for the same targets.</param>
    public static IRestingRule CreateRestingRule(
        ImmutableArray<TargetObject> targets,
        IVisibleTargetObjects visibleTargets,
        AlsoRemoveSettings settings,
        ModTrustTable trust,
        LoadOrderPlugins plugins,
        OtherModsObjectsSettings otherModsObjects,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
        HeldUpTargets heldUp,
        Execution execution) =>
        OtherModsObjects.CreateRestingRule(
            targets,
            visibleTargets,
            new RestingRule(targets, visibleTargets, settings, new OtherModsObjectRules(trust, plugins, shapes), shapes, triangles, surfaces, terrain, objectsOfAnyPlugin, execution),
            heldUp,
            new OtherModsObjectsContext(trust, plugins, otherModsObjects, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin, execution));

    /// <summary>
    /// KeepSupportingObject: the target objects other mods' objects rest on, directly or through other target objects, each where
    /// it stands before any decision; switched off otherwise (see <see cref="OtherModsObjects"/>).
    /// </summary>
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
        Execution execution) =>
        OtherModsObjects.FindHeldUpTargets(
            targets, visibleTargets, new OtherModsObjectsContext(trust, plugins, settings, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin, execution));

    /// <summary>
    /// RemoveFloatingObject and PullObjectToTheGround: what becomes of the other mods' objects left without support once the
    /// also-remove rounds are decided, and the also-remove rounds for the target objects resting on or touching those taken away;
    /// switched off with KeepSupportingObject (see <see cref="OtherModsObjects"/>). The rounds are decided on a working copy of
    /// <paramref name="decided"/>, which never changes, and proposed for the runner to apply.
    /// </summary>
    /// <param name="targets">Where they stood before any decision.</param>
    /// <param name="targetsAsMeasured">Each target object kept shrunk or turned at its new placement, as the rounds measure it.</param>
    /// <param name="decided">The decisions of every removal round so far, the also-remove rounds included.</param>
    /// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
    /// <param name="visibleTargetsAsMeasured">The index of <paramref name="targetsAsMeasured"/>.</param>
    /// <param name="indexTargets">The index of the given target objects.</param>
    /// <param name="trust">Decides whose objects are judged.</param>
    /// <param name="objectsOfAnyPlugin">Asked only when a target object was removed, shrunk or turned.</param>
    public static OtherModsObjectsResult SettleOtherModsObjects(
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
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
        Execution execution) =>
        OtherModsObjects.SettleLeftWithoutSupport(
            new DecidedTargets(targets, targetsAsMeasured, decided, visibleTargets, visibleTargetsAsMeasured, indexTargets),
            alsoRemove,
            new OtherModsObjectsContext(trust, plugins, settings, shapes, triangles, surfaces, terrain, objectsOfAnyPlugin, execution));

    private static RestingObjectsRun DecideRounds(
        RestingObjectsInput input,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaces,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> askObjectsOfAnyPlugin)
    {
        var (earlierRounds, seedRounds, settings) = (input.EarlierRounds, input.SeedRounds, input.Settings);
        var (run, roundsTime) = TimeRounds(
            NeedsObjectsOfAnyPlugin(earlierRounds, seedRounds, settings),
            askObjectsOfAnyPlugin,
            input.TimeSinceStart,
            objectsOfAnyPlugin => DecideOnWorkingCopy(
                Prepare(
                    earlierRounds,
                    seedRounds,
                    new RoundsInput(input.Targets, objectsOfAnyPlugin, shapes, triangles, surfaces, terrain, input.Execution),
                    settings),
                earlierRounds,
                seedRounds));
        return run with { Result = run.Result with { RoundsTime = roundsTime } };
    }

    /// <summary>What <paramref name="decideRounds"/> returns, with the time it took.</summary>
    /// <remarks>
    /// The objects of any plugin are asked for before the time starts, because the first ask builds the supporters' bounds
    /// index, which the "Also remove" time leaves out.
    /// </remarks>
    /// <param name="decideRounds">Gets the objects of any plugin when <paramref name="needsObjectsOfAnyPlugin"/>, else null.</param>
    internal static (TRounds Rounds, TimeSpan Time) TimeRounds<TRounds>(
        bool needsObjectsOfAnyPlugin,
        Func<IVisibleObjectsOfAnyPlugin> askObjectsOfAnyPlugin,
        Func<TimeSpan> timeSinceStart,
        Func<IVisibleObjectsOfAnyPlugin?, TRounds> decideRounds)
    {
        var objectsOfAnyPlugin = needsObjectsOfAnyPlugin ? askObjectsOfAnyPlugin() : null;
        var start = timeSinceStart();
        var rounds = decideRounds(objectsOfAnyPlugin);
        return (rounds, timeSinceStart() - start);
    }

    /// <param name="earlierRounds">The decisions with the earlier removal rounds.</param>
    /// <param name="seedRounds">The rounds of <paramref name="earlierRounds"/> whose removals seed this step.</param>
    internal static RestingObjectsPlan Prepare(IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds, RoundsInput input, AlsoRemoveSettings settings)
    {
        if (SeedsOf(earlierRounds, seedRounds).IsEmpty) return new RestingObjectsPlan(settings.Mode, HadSeeds: false, Rounds: null, Search: null);

        var spread = PrepareFrom(earlierRounds, input, settings);
        return new RestingObjectsPlan(settings.Mode, HadSeeds: true, spread.Rounds, spread.Search);
    }

    /// <summary>The rounds that spread from the seeds they are given as the mode says; Nothing has none.</summary>
    /// <param name="decided">The decisions the seeds were removed in; the objects they kept never take part in the touch rounds.</param>
    internal static SpreadRule PrepareFrom(IRemovalDecisions decided, RoundsInput input, AlsoRemoveSettings settings) =>
        settings.Mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt => PrepareTouchRounds(decided, input, settings),
            FollowUpRemovalMode.OnlyWhatLosesItsSupport => PrepareSupportRounds(input, settings),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
        };

    /// <summary>The plan's rounds, decided on a working copy of <paramref name="earlierRounds"/>, which never changes.</summary>
    internal static RestingObjectsRun DecideOnWorkingCopy(RestingObjectsPlan plan, IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds)
    {
        if (plan.Rounds is not { } rounds) return SummarizeRounds(plan, earlierRounds, seedRounds, earlierRounds, []);

        var (decisions, decided) = earlierRounds.WorkingCopy().DecideRoundsUntilNothingRemoved(rounds, SeedsOf(earlierRounds, seedRounds));
        return SummarizeRounds(plan, earlierRounds, seedRounds, decisions, decided);
    }

    /// <summary>The result of the rounds decided for <paramref name="plan"/>.</summary>
    /// <param name="earlierRounds">The decisions the rounds started from.</param>
    /// <param name="seedRounds">The rounds of <paramref name="earlierRounds"/> whose removals seeded the rounds.</param>
    /// <param name="decisions">The decisions with the rounds; <paramref name="earlierRounds"/> when none ran.</param>
    /// <param name="rounds">One per round, in round order.</param>
    internal static RestingObjectsRun SummarizeRounds(
        RestingObjectsPlan plan,
        IRemovalDecisions earlierRounds,
        ImmutableArray<Round> seedRounds,
        IRemovalDecisions decisions,
        ImmutableArray<RoundProposals<RoundDetails>> rounds)
    {
        if (plan.Rounds == null) return new RestingObjectsRun(WithoutRounds(StepOutcome.Performed, plan.Mode, plan.HadSeeds, failure: null), earlierRounds, Search: null);

        return new RestingObjectsRun(SummarizeSeededRounds(plan.Mode, seedRounds, decisions.RoundsAfter(earlierRounds), rounds, decisions), decisions, plan.Search);
    }

    /// <summary>The result of rounds that had seeds, without their time and touch chains.</summary>
    /// <param name="seedRounds">The rounds whose removals seeded the rounds.</param>
    /// <param name="roundsOfDecisions">The rounds as <paramref name="decisions"/> numbers them, in round order.</param>
    /// <param name="rounds">One per round, in round order.</param>
    /// <param name="decisions">The decisions with the rounds.</param>
    internal static RestingObjectsResult SummarizeSeededRounds(
        FollowUpRemovalMode mode,
        ImmutableArray<Round> seedRounds,
        ImmutableArray<Round> roundsOfDecisions,
        ImmutableArray<RoundProposals<RoundDetails>> rounds,
        IRemovalDecisions decisions)
    {
        var evidence = rounds.Select(round => round.Details).ToImmutableArray();
        var work = mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt => new RestingObjectsWork(evidence.Length, Candidates: 0, Evaluations: 0, KeptWithoutContacts: 0, SumPairWork(evidence)),
            FollowUpRemovalMode.OnlyWhatLosesItsSupport => CountSupportWork(evidence, decisions),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {mode}."),
        };
        return new RestingObjectsResult(
            StepOutcome.Performed,
            mode,
            HadSeeds: true,
            seedRounds,
            roundsOfDecisions,
            [.. rounds.Select(round => round.Proposals)],
            evidence,
            work,
            RoundsTime: TimeSpan.Zero,
            TouchChains: null,
            Failure: null,
            TouchChainsFailure: null,
            ReportFileDetails.SwitchedOff);
    }

    /// <summary>Whether <see cref="Prepare"/> needs <see cref="RoundsInput.ObjectsOfAnyPlugin"/>: only the support rounds use them, and only when they have seeds.</summary>
    /// <param name="earlierRounds">The decisions with the earlier removal rounds.</param>
    /// <param name="seedRounds">The rounds of <paramref name="earlierRounds"/> that seed this step.</param>
    internal static bool NeedsObjectsOfAnyPlugin(IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds, AlsoRemoveSettings settings) =>
        ReadsObjectsOfAnyPlugin(settings.Mode) && !SeedsOf(earlierRounds, seedRounds).IsEmpty;

    /// <summary>Whether rounds of <paramref name="mode"/> with seeds read <see cref="RoundsInput.ObjectsOfAnyPlugin"/>.</summary>
    internal static bool ReadsObjectsOfAnyPlugin(FollowUpRemovalMode mode) => mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport;

    /// <summary>Finding the touch chains tests the touching pairs again, so only the detailed log and the report files ask for them.</summary>
    internal static bool NeedsTouchChains(RestingObjectsResult result, AlsoRemoveSettings settings) =>
        result.Mode == FollowUpRemovalMode.EverythingTouchingIt && result.HadSeeds && settings.TouchChainsShown;

    private static RestingObjectsResult WithTouchChains(RestingObjectsRun run, RestingObjectsInput input) =>
        WithTouchChains(run.Result, input.Settings, () => TouchChains.Find(run, input.Targets.Length, input.Execution));

    /// <remarks>Stopped on its own, so a failure here keeps the rounds.</remarks>
    internal static RestingObjectsResult WithTouchChains(RestingObjectsResult result, AlsoRemoveSettings settings, Func<TouchChainSet> findTouchChains)
    {
        if (!NeedsTouchChains(result, settings)) return result;
        return Failures.Attempt(
            () => result with { TouchChains = findTouchChains() },
            failure => result with { TouchChainsFailure = PartFailure.OfWholePart(RestingObjectsParts.TouchChains, failure) });
    }

    /// <remarks>A step whose rounds or touch chains failed computes no details; their status says so.</remarks>
    private static ReportFileDetails FindReportFileDetails(
        RestingObjectsRun run,
        RestingObjectsInput input,
        IBaseFacts bases,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles)
    {
        if (input.ReportFiles != StepOutcome.Performed) return ReportFileDetails.SwitchedOff;
        if (run.Result.Outcome == StepOutcome.StoppedByUnexpectedError) return ReportFileDetails.StoppedByEarlierFailure;
        if (run.Result.TouchChainsFailure is not null) return ReportFileDetails.StoppedByEarlierFailure;

        var detailsInput = new ReportFileDetailsInput(run, run.Result.TouchChains, input.Targets, shapes, triangles, bases, input.Execution);
        return Failures.Attempt(
            () => ExplanationFinder.Compute(detailsInput),
            failure => new ReportFileDetails(
                StepOutcome.StoppedByUnexpectedError, Touch: null, MeshOrigins: null, PartFailure.OfWholePart(RestingObjectsParts.ReportFileDetails, failure)));
    }

    /// <summary>The step without rounds: switched off, or stopped by <paramref name="failure"/>; it proposes nothing.</summary>
    private static RestingObjectsRun NotPerformed(RestingObjectsInput input, StepOutcome outcome, PartFailure? failure) =>
        new(WithoutRounds(outcome, input.Settings.Mode, hadSeeds: false, failure), input.EarlierRounds, Search: null);

    internal static RestingObjectsResult WithoutRounds(StepOutcome outcome, FollowUpRemovalMode mode, bool hadSeeds, PartFailure? failure) =>
        new(
            outcome,
            mode,
            hadSeeds,
            [],
            [],
            [],
            [],
            new RestingObjectsWork(0, 0, 0, 0, PairTestStats.Zero),
            RoundsTime: TimeSpan.Zero,
            TouchChains: null,
            failure,
            TouchChainsFailure: null,
            ReportFileDetails.SwitchedOff);

    /// <summary>The objects the also-remove rounds start from: those the seed rounds removed.</summary>
    private static ImmutableArray<TargetId> SeedsOf(IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds) =>
        [.. seedRounds.SelectMany(round => earlierRounds.LeftIn(round))];

    private static SpreadRule PrepareTouchRounds(IRemovalDecisions earlierRounds, RoundsInput input, AlsoRemoveSettings settings)
    {
        var search = CreateTouchSearch(earlierRounds, input, settings);
        return new SpreadRule(new TouchRule(search), search);
    }

    private static SpreadRule PrepareSupportRounds(RoundsInput input, AlsoRemoveSettings settings) =>
        new(CreateSupportRule(input, settings), Search: null);

    /// <remarks>The objects kept in the earlier rounds never take part, so the touch never spreads through them.</remarks>
    private static TouchSearch CreateTouchSearch(IRemovalDecisions earlierRounds, RoundsInput input, AlsoRemoveSettings settings) =>
        TouchSearch.Create(
            input.Targets,
            excluded: [.. earlierRounds.Rounds.SelectMany(round => earlierRounds.KeptIn(round)).Select(target => target.Index)],
            input.Shapes,
            input.Triangles,
            settings.TouchGap,
            input.Exec);

    /// <remarks>Objects kept in the earlier rounds are decided, so they are never candidates, and they keep supporting others.</remarks>
    private static SupportRule CreateSupportRule(RoundsInput input, AlsoRemoveSettings settings)
    {
        var objectsOfAnyPlugin = input.ObjectsOfAnyPlugin ?? throw new ArgumentException("OnlyWhatLosesItsSupport needs the objects of any plugin.", nameof(input));
        var search = TouchSearch.Create(
            input.Targets,
            excluded: [],
            input.Shapes,
            input.Triangles,
            settings.TouchGap,
            input.Exec);
        var contactFinder = CreateSupportContactFinder(input.Targets, search, objectsOfAnyPlugin, input.Shapes, input.Terrain, input.Surfaces, settings.TouchGap);
        return new SupportRule(search, contactFinder, settings.SupportLostFraction, input.Targets.Length, input.Exec);
    }

    internal static SupportContactFinder CreateSupportContactFinder(
        IReadOnlyList<TargetObject> targets,
        TouchSearch search,
        IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin,
        IBaseObjectShapes shapes,
        ITerrainHeights terrain,
        ISurfaceSamples surfaces,
        float touchGap)
    {
        var supporterFinder = new SupporterFinder(targets, search, objectsOfAnyPlugin, shapes, touchGap);
        return new SupportContactFinder(targets, search.MeshPaths, supporterFinder, new SurfaceContacts(terrain, search.Cache, surfaces, touchGap));
    }

    private static RestingObjectsWork CountSupportWork(ImmutableArray<RoundDetails> evidence, IRemovalDecisions decisions)
    {
        var evaluations = evidence.Cast<SupportRoundDetails>().SelectMany(round => round.Evaluations).ToList();
        return new RestingObjectsWork(
            evidence.Length,
            evaluations.Select(evaluation => evaluation.TargetIndex).Distinct().Count(),
            evaluations.Count,
            CountKeptWithoutContacts(evaluations, decisions),
            SumPairWork(evidence));
    }

    /// <summary>Unprotected candidates kept because no surface sample touched any supporter.</summary>
    private static int CountKeptWithoutContacts(IEnumerable<SupportEvaluation> evaluations, IRemovalDecisions decisions) =>
        evaluations
            .Where(evaluation => evaluation.Contacts.ContactPoints == 0)
            .Select(evaluation => new TargetId(evaluation.TargetIndex))
            .Where(target => !decisions.IsProtected(target))
            .Distinct()
            .Count();

    private static PairTestStats SumPairWork(ImmutableArray<RoundDetails> evidence) =>
        Work.Sum(evidence.Select(round => round switch
        {
            TouchRoundDetails touch => touch.Work,
            SupportRoundDetails support => support.Work,
            _ => throw new UnreachableException($"Unknown follow-up evidence {round.GetType().Name}."),
        }));
}
