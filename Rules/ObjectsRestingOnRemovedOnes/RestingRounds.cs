using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove rounds: their rule as the mode says, deciding them on
/// a working copy, their time, and the result they sum up to.
/// </summary>
internal static class RestingRounds
{
    /// <summary>What <paramref name="decideRounds"/> returns, with the time it took.</summary>
    /// <remarks>
    /// The objects of any plugin are asked for before the time starts, because the first ask builds the supporters' bounds
    /// index, which the "Also remove" time leaves out.
    /// </remarks>
    /// <param name="decideRounds">Gets the objects of any plugin when <paramref name="needsObjectsOfAnyPlugin"/>, else null.</param>
    public static (TRounds Rounds, TimeSpan Time) TimeRounds<TRounds>(
        bool needsObjectsOfAnyPlugin,
        Func<IVisibleObjectsOfAnyPlugin> askObjectsOfAnyPlugin,
        Func<TimeSpan> timeSinceStart,
        Func<IVisibleObjectsOfAnyPlugin?, TRounds> decideRounds)
    {
        var objectsOfAnyPlugin = needsObjectsOfAnyPlugin ? askObjectsOfAnyPlugin() : null;
        return Timed.Run(timeSinceStart, () => decideRounds(objectsOfAnyPlugin));
    }

    /// <param name="earlierRounds">The decisions with the earlier removal rounds.</param>
    /// <param name="seedRounds">The rounds of <paramref name="earlierRounds"/> whose removals seed this step.</param>
    public static RestingObjectsPlan Prepare(IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds, RoundsInput input, AlsoRemoveSettings settings)
    {
        if (SeedsOf(earlierRounds, seedRounds).IsEmpty) return new RestingObjectsPlan(settings.Mode, HadSeeds: false, Rounds: null, Search: null);

        var spread = PrepareFrom(earlierRounds, input, settings);
        return new RestingObjectsPlan(settings.Mode, HadSeeds: true, spread.Rounds, spread.Search);
    }

    /// <summary>The rounds that spread from the seeds they are given as the mode says; Nothing has none.</summary>
    /// <param name="decided">The decisions the seeds were removed in; the objects they kept never take part in the touch rounds.</param>
    public static SpreadRule PrepareFrom(IRemovalDecisions decided, RoundsInput input, AlsoRemoveSettings settings) =>
        settings.Mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt => PrepareTouchRounds(decided, input, settings),
            FollowUpRemovalMode.OnlyWhatLosesItsSupport => PrepareSupportRounds(input, settings),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
        };

    /// <summary>The plan's rounds, decided on a working copy of <paramref name="earlierRounds"/>, which never changes.</summary>
    public static RestingObjectsRun DecideOnWorkingCopy(RestingObjectsPlan plan, IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds)
    {
        if (plan.Rounds is not { } rounds) return SummarizeRounds(plan, earlierRounds, seedRounds, earlierRounds, []);

        var (decisions, decided) = earlierRounds.WorkingCopy().DecideRoundsUntilNothingRemoved(AlsoRemoveRounds.Opening, rounds, SeedsOf(earlierRounds, seedRounds));
        return SummarizeRounds(plan, earlierRounds, seedRounds, decisions, decided);
    }

    /// <summary>The result of the rounds decided for <paramref name="plan"/>.</summary>
    /// <param name="earlierRounds">The decisions the rounds started from.</param>
    /// <param name="seedRounds">The rounds of <paramref name="earlierRounds"/> whose removals seeded the rounds.</param>
    /// <param name="decisions">The decisions with the rounds; <paramref name="earlierRounds"/> when none ran.</param>
    /// <param name="rounds">One per round, in round order.</param>
    public static RestingObjectsRun SummarizeRounds(
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
    public static RestingObjectsResult SummarizeSeededRounds(
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
    public static bool NeedsObjectsOfAnyPlugin(IRemovalDecisions earlierRounds, ImmutableArray<Round> seedRounds, AlsoRemoveSettings settings) =>
        ReadsObjectsOfAnyPlugin(settings.Mode) && !SeedsOf(earlierRounds, seedRounds).IsEmpty;

    /// <summary>Whether rounds of <paramref name="mode"/> with seeds read <see cref="RoundsInput.ObjectsOfAnyPlugin"/>.</summary>
    public static bool ReadsObjectsOfAnyPlugin(FollowUpRemovalMode mode) => mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport;

    /// <summary>Finding the touch chains tests the touching pairs again, so only the detailed log and the report files ask for them.</summary>
    public static bool NeedsTouchChains(RestingObjectsResult result, AlsoRemoveSettings settings) =>
        result.Mode == FollowUpRemovalMode.EverythingTouchingIt && result.HadSeeds && settings.TouchChainsShown;

    /// <remarks>Stopped on its own, so a failure here keeps the rounds.</remarks>
    public static RestingObjectsResult WithTouchChains(RestingObjectsResult result, AlsoRemoveSettings settings, Func<TouchChainSet> findTouchChains)
    {
        if (!NeedsTouchChains(result, settings)) return result;
        return Failures.Attempt(
            () => result with { TouchChains = findTouchChains() },
            failure => result with { TouchChainsFailure = PartFailure.OfWholePart(RestingObjectsParts.TouchChains, failure) });
    }

    public static RestingObjectsResult WithoutRounds(StepOutcome outcome, FollowUpRemovalMode mode, bool hadSeeds, PartFailure? failure) =>
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
            input.Reads.Targets,
            excluded: [.. earlierRounds.Rounds.SelectMany(round => earlierRounds.KeptIn(round)).Select(target => target.Index)],
            input.Reads.Shapes,
            input.Reads.Triangles,
            settings.TouchGap,
            input.Reads.Exec);

    /// <remarks>Objects kept in the earlier rounds are decided, so they are never candidates, and they keep supporting others.</remarks>
    private static SupportRule CreateSupportRule(RoundsInput input, AlsoRemoveSettings settings)
    {
        var objectsOfAnyPlugin = input.ObjectsOfAnyPlugin ?? throw new ArgumentException("OnlyWhatLosesItsSupport needs the objects of any plugin.", nameof(input));
        var support = SupportSearch.Create(input.Reads, () => objectsOfAnyPlugin, settings.TouchGap);
        return new SupportRule(support.Search, support.Contacts, settings.SupportLostFraction, input.Reads.Targets.Length, input.Reads.Exec);
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
