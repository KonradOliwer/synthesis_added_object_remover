using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>
/// The follow-up rounds: seeded with the too-close round's removals, they remove what goes with
/// them, as the follow-up mode says, until a round removes nothing.
/// </summary>
internal static class FollowUp
{
    /// <param name="afterClashes">The ledger whose last round is the too-close round.</param>
    public static FollowUpResult Run(Ledger afterClashes, FollowUpInput input, FollowUpOptions options)
    {
        var seedRound = afterClashes.Rounds[^1];
        var seeds = afterClashes.RemovedIn(seedRound).Select(target => target.Index).ToList();
        var hadSeeds = seeds.Count > 0;
        if (!hadSeeds || options.Mode == FollowUpRemovalMode.Nothing) return WithoutRounds(afterClashes, options.Mode, hadSeeds);

        return options.Mode switch
        {
            FollowUpRemovalMode.EverythingTouching => RunTouchRounds(afterClashes, seeds, input, options),
            FollowUpRemovalMode.ObjectsSupportedByIt => RunSupportRounds(afterClashes, seeds, input, options),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {options.Mode}."),
        };
    }

    /// <summary>Runs the touch rounds with the given rule; the result keeps the search for the touch components.</summary>
    internal static FollowUpResult RunTouchRounds(Ledger afterClashes, TouchSearch search, IFollowUpRule rule)
    {
        var (ledger, evidence) = Cascade.Run(afterClashes, rule);
        var work = new FollowUpWork(evidence.Length, Candidates: 0, Evaluations: 0, KeptWithoutContacts: 0, SumPairWork(evidence));
        return new FollowUpResult(
            FollowUpRemovalMode.EverythingTouching, HadSeeds: true, ledger, RoundsAddedTo(afterClashes, ledger), evidence, work, new FollowUpContext(search));
    }

    /// <summary>The connected components of the touch cascade, with their stats; finding them tests touching pairs again.</summary>
    /// <param name="followUp">A result of the touch rounds.</param>
    public static TouchComponentSet Components(FollowUpResult followUp, int targetCount, Execution execution) =>
        TouchComponents.Find(followUp, targetCount, execution);

    private static FollowUpResult RunTouchRounds(Ledger afterClashes, IReadOnlyList<int> seeds, FollowUpInput input, FollowUpOptions options)
    {
        var search = CreateTouchSearch(afterClashes, seeds, input, options);
        return RunTouchRounds(afterClashes, search, new TouchRule(search, input.Exec, input.Timer));
    }

    private static FollowUpResult RunSupportRounds(Ledger afterClashes, IReadOnlyList<int> seeds, FollowUpInput input, FollowUpOptions options)
    {
        var rule = input.Timer.Time(TimedPhase.AnchoringSetup, () => CreateSupportRule(seeds, input, options));
        var (ledger, evidence) = Cascade.Run(afterClashes, rule);
        var evaluations = evidence.Cast<SupportRound>().SelectMany(round => round.Evaluations).ToList();
        var work = new FollowUpWork(
            evidence.Length,
            evaluations.Select(evaluation => evaluation.TargetIndex).Distinct().Count(),
            evaluations.Count,
            CountKeptWithoutContacts(evaluations, ledger),
            SumPairWork(evidence));
        return new FollowUpResult(
            FollowUpRemovalMode.ObjectsSupportedByIt, HadSeeds: true, ledger, RoundsAddedTo(afterClashes, ledger), evidence, work, Context: null);
    }

    private static FollowUpResult WithoutRounds(Ledger afterClashes, FollowUpRemovalMode mode, bool hadSeeds) =>
        new(mode, hadSeeds, afterClashes, [], [], new FollowUpWork(0, 0, 0, 0, PairTestStats.Zero), Context: null);

    /// <remarks>The objects held in the too-close round never take part, so the touch never spreads through them.</remarks>
    private static TouchSearch CreateTouchSearch(Ledger afterClashes, IReadOnlyList<int> seeds, FollowUpInput input, FollowUpOptions options) =>
        input.Timer.Time(TimedPhase.TouchSetup, () => TouchSearch.Create(
            input.Targets,
            input.Looks,
            input.Protection.Groups.CollectReachableSpaces(input.Targets, seeds),
            excluded: [.. afterClashes.HeldIn(afterClashes.Rounds[^1]).Select(target => target.Index)],
            input.Shapes,
            input.Triangles,
            options.TouchGap,
            input.Exec));

    /// <remarks>Objects held in the too-close round are decided, so they are never candidates, and they keep supporting others.</remarks>
    private static SupportRule CreateSupportRule(IReadOnlyList<int> seeds, FollowUpInput input, FollowUpOptions options)
    {
        var solids = input.Solids ?? throw new ArgumentException("ObjectsSupportedByIt needs the solids.", nameof(input));
        var search = TouchSearch.Create(
            input.Targets,
            input.Looks,
            input.Protection.Groups.CollectReachableSpaces(input.Targets, seeds),
            excluded: [],
            input.Shapes,
            input.Triangles,
            options.TouchGap,
            input.Exec);
        var supporterFinder = new AnchoringSupporterFinder(input.Targets, search, solids, input.Shapes, options.TouchGap);
        var contactFinder = new AnchoringContactFinder(
            input.Targets, search.MeshPaths, supporterFinder, input.Terrain, search.Cache, options.TouchGap);
        return new SupportRule(search, contactFinder, options.SupportLostFraction, input.Targets.Length, input.Exec, input.Timer);
    }

    /// <summary>Unprotected candidates kept because no surface sample touched any supporter.</summary>
    private static int CountKeptWithoutContacts(IEnumerable<SupportEvaluation> evaluations, Ledger ledger) =>
        evaluations
            .Where(evaluation => evaluation.Contacts.ContactPoints == 0)
            .Select(evaluation => new TargetId(evaluation.TargetIndex))
            .Where(target => !ledger.IsProtected(target))
            .Distinct()
            .Count();

    private static PairTestStats SumPairWork(ImmutableArray<RoundEvidence> evidence) =>
        PairTestStats.Sum(evidence.Select(round => round switch
        {
            TouchRound touch => touch.Work,
            SupportRound support => support.Work,
            _ => throw new UnreachableException($"Unknown follow-up evidence {round.GetType().Name}."),
        }));

    /// <summary>The rounds <paramref name="after"/> has beyond <paramref name="before"/>, an older view of the same run.</summary>
    private static ImmutableArray<Round> RoundsAddedTo(Ledger before, Ledger after) => [.. after.Rounds.Skip(before.Rounds.Length)];
}
