using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove rounds: seeded with the too-close round's removals, they remove what goes with
/// them, as the also-remove mode says, until a round removes nothing. The step proposes the rounds'
/// rule; the runner applies it to the decisions.
/// </summary>
internal static class RestingObjects
{
    /// <param name="afterTooClose">The decisions whose last round is the too-close round.</param>
    public static RestingObjectsPlan Prepare(IRemovalDecisions afterTooClose, RestingObjectsInput input, AlsoRemoveSettings options)
    {
        var seedRound = afterTooClose.Rounds[^1];
        var seeds = afterTooClose.RemovedIn(seedRound).Select(target => target.Index).ToList();
        var hadSeeds = seeds.Count > 0;
        if (!hadSeeds || options.Mode == FollowUpRemovalMode.Nothing) return new RestingObjectsPlan(options.Mode, hadSeeds, Rounds: null, Search: null);

        return options.Mode switch
        {
            FollowUpRemovalMode.EverythingTouching => PrepareTouchRounds(afterTooClose, seeds, input, options),
            FollowUpRemovalMode.ObjectsSupportedByIt => PrepareSupportRounds(seeds, input, options),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {options.Mode}."),
        };
    }

    /// <summary>The result of the rounds the runner applied for <paramref name="plan"/>.</summary>
    /// <param name="afterTooClose">The decisions the rounds started from.</param>
    /// <param name="decisions">The decisions with the rounds; <paramref name="afterTooClose"/> when none ran.</param>
    /// <param name="evidence">One per round, in round order.</param>
    public static RestingObjectsRun Finish(
        RestingObjectsPlan plan, IRemovalDecisions afterTooClose, IRemovalDecisions decisions, ImmutableArray<RoundDetails> evidence)
    {
        if (plan.Rounds == null) return new RestingObjectsRun(WithoutRounds(plan, afterTooClose), Search: null);

        var work = plan.Mode switch
        {
            FollowUpRemovalMode.EverythingTouching => new RestingObjectsWork(evidence.Length, Candidates: 0, Evaluations: 0, KeptWithoutContacts: 0, SumPairWork(evidence)),
            FollowUpRemovalMode.ObjectsSupportedByIt => CountSupportWork(evidence, decisions),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {plan.Mode}."),
        };
        var result = new RestingObjectsResult(plan.Mode, HadSeeds: true, decisions, decisions.RoundsAfter(afterTooClose), evidence, work);
        return new RestingObjectsRun(result, plan.Search);
    }

    /// <summary>The connected chains of the touch cascade, with their statistics; finding them tests touching pairs again.</summary>
    /// <param name="run">A run of the touch rounds.</param>
    public static TouchChainSet FindTouchChains(RestingObjectsRun run, int targetCount, Execution execution) =>
        TouchChains.Find(run, targetCount, execution);

    private static RestingObjectsPlan PrepareTouchRounds(IRemovalDecisions afterTooClose, IReadOnlyList<int> seeds, RestingObjectsInput input, AlsoRemoveSettings options)
    {
        var search = CreateTouchSearch(afterTooClose, seeds, input, options);
        return new RestingObjectsPlan(FollowUpRemovalMode.EverythingTouching, HadSeeds: true, new TouchRule(search), search);
    }

    private static RestingObjectsPlan PrepareSupportRounds(IReadOnlyList<int> seeds, RestingObjectsInput input, AlsoRemoveSettings options) =>
        new(FollowUpRemovalMode.ObjectsSupportedByIt, HadSeeds: true, CreateSupportRule(seeds, input, options), Search: null);

    private static RestingObjectsResult WithoutRounds(RestingObjectsPlan plan, IRemovalDecisions afterTooClose) =>
        new(plan.Mode, plan.HadSeeds, afterTooClose, [], [], new RestingObjectsWork(0, 0, 0, 0, PairTestStats.Zero));

    /// <remarks>The objects kept in the too-close round never take part, so the touch never spreads through them.</remarks>
    private static TouchSearch CreateTouchSearch(IRemovalDecisions afterTooClose, IReadOnlyList<int> seeds, RestingObjectsInput input, AlsoRemoveSettings options) =>
        TouchSearch.Create(
            input.Targets,
            ReachableSpaces.CollectReachableSpaces(input.Protection.Groups, input.Targets, seeds),
            excluded: [..afterTooClose.KeptIn(afterTooClose.Rounds[^1]).Select(target => target.Index)],
            input.Shapes,
            input.Triangles,
            options.TouchGap,
            input.Exec);

    /// <remarks>Objects kept in the too-close round are decided, so they are never candidates, and they keep supporting others.</remarks>
    private static SupportRule CreateSupportRule(IReadOnlyList<int> seeds, RestingObjectsInput input, AlsoRemoveSettings options)
    {
        var objectsOfAnyPlugin = input.ObjectsOfAnyPlugin ?? throw new ArgumentException("ObjectsSupportedByIt needs the objects of any plugin.", nameof(input));
        var search = TouchSearch.Create(
            input.Targets,
            ReachableSpaces.CollectReachableSpaces(input.Protection.Groups, input.Targets, seeds),
            excluded: [],
            input.Shapes,
            input.Triangles,
            options.TouchGap,
            input.Exec);
        var supporterFinder = new AnchoringSupporterFinder(input.Targets, search, objectsOfAnyPlugin, input.Shapes, options.TouchGap);
        var contactFinder = new AnchoringContactFinder(
            input.Targets, search.MeshPaths, supporterFinder, input.Terrain, search.Cache, options.TouchGap);
        return new SupportRule(search, contactFinder, options.SupportLostFraction, input.Targets.Length, input.Exec);
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
