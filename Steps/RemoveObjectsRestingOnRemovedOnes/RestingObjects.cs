using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove step: seeded with the removals of every earlier removal round, its rounds remove what
/// goes with them, as the also-remove mode says, until a round removes nothing. It decides the rounds on its own copy
/// of the decisions and proposes them as one proposal, which the runner applies to the run's decisions.
/// </summary>
public static class RestingObjects
{
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
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin)
    {
        if (input.Settings.Mode == FollowUpRemovalMode.Nothing) return NotPerformed(input, StepOutcome.SwitchedOff, failure: null).Result;

        var rounds = Failures.Attempt(
            () => DecideRounds(input, shapes, triangles, terrain, objectsOfAnyPlugin),
            failure => NotPerformed(input, StepOutcome.StoppedByUnexpectedError, new PartFailure(RestingObjectsParts.AlsoRemove, failure, Subject: null)));
        var run = rounds with { Result = WithTouchChains(rounds, input) };
        return run.Result with { ReportFileDetails = FindReportFileDetails(run, input, bases, shapes, triangles) };
    }

    /// <remarks>
    /// The objects of any plugin are asked for before the rounds' time starts, because the first ask builds the
    /// supporters' bounds index, which the "Also remove" time leaves out.
    /// </remarks>
    private static RestingObjectsRun DecideRounds(
        RestingObjectsInput input,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ITerrainHeights terrain,
        Func<IVisibleObjectsOfAnyPlugin> askObjectsOfAnyPlugin)
    {
        var (earlierRounds, settings) = (input.EarlierRounds, input.Settings);
        var objectsOfAnyPlugin = NeedsObjectsOfAnyPlugin(earlierRounds, settings) ? askObjectsOfAnyPlugin() : null;
        var start = input.TimeSinceStart();
        var plan = Prepare(
            earlierRounds,
            new RoundsInput(input.Targets, input.ObjectsToKeep, objectsOfAnyPlugin, shapes, triangles, terrain, input.Execution),
            settings);
        var run = DecideOnWorkingCopy(plan, earlierRounds);
        return run with { Result = run.Result with { RoundsTime = input.TimeSinceStart() - start } };
    }

    /// <param name="earlierRounds">The decisions with the earlier removal rounds, which seed this step.</param>
    internal static RestingObjectsPlan Prepare(IRemovalDecisions earlierRounds, RoundsInput input, AlsoRemoveSettings settings)
    {
        var seeds = SeedsOf(earlierRounds).Select(target => target.Index).ToList();
        var hadSeeds = seeds.Count > 0;
        if (!hadSeeds) return new RestingObjectsPlan(settings.Mode, hadSeeds, Rounds: null, Search: null);

        return settings.Mode switch
        {
            FollowUpRemovalMode.EverythingTouching => PrepareTouchRounds(earlierRounds, seeds, input, settings),
            FollowUpRemovalMode.ObjectsSupportedByIt => PrepareSupportRounds(seeds, input, settings),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
        };
    }

    /// <summary>The plan's rounds, decided on a working copy of <paramref name="earlierRounds"/>, which never changes.</summary>
    internal static RestingObjectsRun DecideOnWorkingCopy(RestingObjectsPlan plan, IRemovalDecisions earlierRounds)
    {
        if (plan.Rounds is not { } rounds) return SummarizeRounds(plan, earlierRounds, earlierRounds, []);

        var (decisions, decided) = earlierRounds.WorkingCopy().DecideRoundsUntilNothingRemoved(rounds, SeedsOf(earlierRounds));
        return SummarizeRounds(plan, earlierRounds, decisions, decided);
    }

    /// <summary>The result of the rounds decided for <paramref name="plan"/>.</summary>
    /// <param name="earlierRounds">The decisions the rounds started from.</param>
    /// <param name="decisions">The decisions with the rounds; <paramref name="earlierRounds"/> when none ran.</param>
    /// <param name="rounds">One per round, in round order.</param>
    internal static RestingObjectsRun SummarizeRounds(
        RestingObjectsPlan plan, IRemovalDecisions earlierRounds, IRemovalDecisions decisions, ImmutableArray<RoundProposals<RoundDetails>> rounds)
    {
        if (plan.Rounds == null) return new RestingObjectsRun(WithoutRounds(StepOutcome.Performed, plan.Mode, plan.HadSeeds, failure: null), earlierRounds, Search: null);

        var evidence = rounds.Select(round => round.Details).ToImmutableArray();
        var work = plan.Mode switch
        {
            FollowUpRemovalMode.EverythingTouching => new RestingObjectsWork(evidence.Length, Candidates: 0, Evaluations: 0, KeptWithoutContacts: 0, SumPairWork(evidence)),
            FollowUpRemovalMode.ObjectsSupportedByIt => CountSupportWork(evidence, decisions),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {plan.Mode}."),
        };
        var result = new RestingObjectsResult(
            StepOutcome.Performed,
            plan.Mode,
            HadSeeds: true,
            earlierRounds.Rounds,
            decisions.RoundsAfter(earlierRounds),
            [.. rounds.Select(round => round.Proposals)],
            evidence,
            work,
            RoundsTime: TimeSpan.Zero,
            TouchChains: null,
            Failure: null,
            TouchChainsFailure: null,
            ReportFileDetails.SwitchedOff);
        return new RestingObjectsRun(result, decisions, plan.Search);
    }

    /// <summary>Whether <see cref="Prepare"/> needs <see cref="RoundsInput.ObjectsOfAnyPlugin"/>: only the support rounds use them, and only when they have seeds.</summary>
    /// <param name="earlierRounds">The decisions with the earlier removal rounds, which seed this step.</param>
    internal static bool NeedsObjectsOfAnyPlugin(IRemovalDecisions earlierRounds, AlsoRemoveSettings settings) =>
        settings.Mode == FollowUpRemovalMode.ObjectsSupportedByIt && !SeedsOf(earlierRounds).IsEmpty;

    /// <summary>Finding the touch chains tests the touching pairs again, so only the detailed log and the report files ask for them.</summary>
    internal static bool NeedsTouchChains(RestingObjectsResult result, AlsoRemoveSettings settings) =>
        result.Mode == FollowUpRemovalMode.EverythingTouching && result.HadSeeds && settings.TouchChainsShown;

    /// <remarks>Stopped on its own, so a failure here keeps the rounds.</remarks>
    private static RestingObjectsResult WithTouchChains(RestingObjectsRun run, RestingObjectsInput input)
    {
        if (!NeedsTouchChains(run.Result, input.Settings)) return run.Result;
        return Failures.Attempt(
            () => run.Result with { TouchChains = TouchChains.Find(run, input.Targets.Length, input.Execution) },
            failure => run.Result with { TouchChainsFailure = new PartFailure(RestingObjectsParts.TouchChains, failure, Subject: null) });
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
                StepOutcome.StoppedByUnexpectedError, Touch: null, MeshOrigins: null, new PartFailure(RestingObjectsParts.ReportFileDetails, failure, Subject: null)));
    }

    /// <summary>The step without rounds: switched off, or stopped by <paramref name="failure"/>; it proposes nothing.</summary>
    private static RestingObjectsRun NotPerformed(RestingObjectsInput input, StepOutcome outcome, PartFailure? failure) =>
        new(WithoutRounds(outcome, input.Settings.Mode, hadSeeds: false, failure), input.EarlierRounds, Search: null);

    private static RestingObjectsResult WithoutRounds(StepOutcome outcome, FollowUpRemovalMode mode, bool hadSeeds, PartFailure? failure) =>
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

    /// <summary>The removals the also-remove rounds start from: those of every earlier round.</summary>
    private static ImmutableArray<TargetId> SeedsOf(IRemovalDecisions earlierRounds) =>
        [.. earlierRounds.Rounds.SelectMany(round => earlierRounds.RemovedIn(round))];

    private static RestingObjectsPlan PrepareTouchRounds(IRemovalDecisions earlierRounds, IReadOnlyList<int> seeds, RoundsInput input, AlsoRemoveSettings settings)
    {
        var search = CreateTouchSearch(earlierRounds, seeds, input, settings);
        return new RestingObjectsPlan(FollowUpRemovalMode.EverythingTouching, HadSeeds: true, new TouchRule(search), search);
    }

    private static RestingObjectsPlan PrepareSupportRounds(IReadOnlyList<int> seeds, RoundsInput input, AlsoRemoveSettings settings) =>
        new(FollowUpRemovalMode.ObjectsSupportedByIt, HadSeeds: true, CreateSupportRule(seeds, input, settings), Search: null);

    /// <remarks>The objects kept in the seed rounds never take part, so the touch never spreads through them.</remarks>
    private static TouchSearch CreateTouchSearch(IRemovalDecisions earlierRounds, IReadOnlyList<int> seeds, RoundsInput input, AlsoRemoveSettings settings) =>
        TouchSearch.Create(
            input.Targets,
            ReachableSpaces.CollectReachableSpaces(input.Protection.Groups, input.Targets, seeds),
            excluded: [.. earlierRounds.Rounds.SelectMany(round => earlierRounds.KeptIn(round)).Select(target => target.Index)],
            input.Shapes,
            input.Triangles,
            settings.TouchGap,
            input.Exec);

    /// <remarks>Objects kept in the seed rounds are decided, so they are never candidates, and they keep supporting others.</remarks>
    private static SupportRule CreateSupportRule(IReadOnlyList<int> seeds, RoundsInput input, AlsoRemoveSettings settings)
    {
        var objectsOfAnyPlugin = input.ObjectsOfAnyPlugin ?? throw new ArgumentException("ObjectsSupportedByIt needs the objects of any plugin.", nameof(input));
        var search = TouchSearch.Create(
            input.Targets,
            ReachableSpaces.CollectReachableSpaces(input.Protection.Groups, input.Targets, seeds),
            excluded: [],
            input.Shapes,
            input.Triangles,
            settings.TouchGap,
            input.Exec);
        var supporterFinder = new SupporterFinder(input.Targets, search, objectsOfAnyPlugin, input.Shapes, settings.TouchGap);
        var contactFinder = new SupportContactFinder(
            input.Targets, search.MeshPaths, supporterFinder, input.Terrain, search.Cache, settings.TouchGap);
        return new SupportRule(search, contactFinder, settings.SupportLostFraction, input.Targets.Length, input.Exec);
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
