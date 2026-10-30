using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The also-remove rounds that follow up removals pass by pass. One rule, built at the first pass with seeds, serves every pass:
/// the touch search builds each space's boxes and pair finder once, and the support rule finds each candidate's contacts once
/// and numbers them on across the passes. Each pass decides its rounds on its own working copy and keeps its seeds, rounds,
/// evidence and time, which <see cref="Summarize"/> merges.
/// </summary>
/// <remarks>
/// The rule is built from the decisions of the first pass with seeds, so the objects kept before it never take part in the touch
/// search, as in the also-remove step; objects kept later stay in the search but are never candidates, because the rounds skip
/// decided objects.
/// </remarks>
/// <param name="targets">Where the rounds measure them, from the decisions the first pass starts from.</param>
/// <param name="timeSinceStart">The run's clock, which measures the rounds.</param>
/// <param name="objectsOfAnyPlugin">Asked only when the support rounds have seeds.</param>
internal sealed class AlsoRemoveFollowUp(
    ImmutableArray<TargetObject> targets,
    AlsoRemoveSettings settings,
    Execution execution,
    Func<TimeSpan> timeSinceStart,
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
    ISurfaceSamples surfaces,
    ITerrainHeights terrain,
    Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin)
    : IAlsoRemoveFollowUp
{
    private readonly List<DecidedPass> _passes = [];
    private SpreadRule? _rule;
    private PartFailure? _failure;

    public FollowUpPass FollowUp(IRemovalDecisions decided, ImmutableArray<TargetId> seeds)
    {
        if (settings.Mode == FollowUpRemovalMode.Nothing || seeds.IsEmpty || _failure is not null) return new FollowUpPass([], _failure);

        return Failures.Attempt(() => RecordPass(DecidePass(decided, seeds)), StopFollowUps);
    }

    /// <remarks>Stopped by its first pass, the follow-ups count as stopped; stopped later, they keep the passes before.</remarks>
    public RestingObjectsResult Summarize(IRemovalDecisions final)
    {
        if (settings.Mode == FollowUpRemovalMode.Nothing) return RestingObjects.WithoutRounds(StepOutcome.SwitchedOff, settings.Mode, hadSeeds: false, failure: null);
        if (_passes.Count == 0)
        {
            var outcome = _failure is null ? StepOutcome.Performed : StepOutcome.StoppedByUnexpectedError;
            return RestingObjects.WithoutRounds(outcome, settings.Mode, hadSeeds: false, _failure);
        }

        var result = RestingObjects.SummarizeSeededRounds(
            settings.Mode,
            [.. _passes.SelectMany(pass => pass.SeedRounds)],
            [.. _passes.SelectMany(pass => pass.Rounds)],
            [.. _passes.SelectMany(pass => pass.RoundProposals)],
            final) with
        {
            RoundsTime = _passes.Aggregate(TimeSpan.Zero, (sum, pass) => sum + pass.Time),
            Failure = _failure,
        };
        return RestingObjects.WithTouchChains(result, settings, () => FindTouchChains(final, result.Evidence));
    }

    private DecidedPass DecidePass(IRemovalDecisions decided, ImmutableArray<TargetId> seeds)
    {
        var ((decisions, roundProposals), time) = RestingObjects.TimeRounds(
            _rule is null && RestingObjects.ReadsObjectsOfAnyPlugin(settings.Mode),
            objectsOfAnyPlugin,
            timeSinceStart,
            objects => decided.WorkingCopy().DecideRoundsUntilNothingRemoved(RuleFor(decided, objects).Rounds, seeds));
        return new DecidedPass(seeds, SeedRoundsOf(decided, seeds), decisions.RoundsAfter(decided), roundProposals, time);
    }

    private SpreadRule RuleFor(IRemovalDecisions decided, IVisibleObjectsOfAnyPlugin? objects) =>
        _rule ??= RestingObjects.PrepareFrom(decided, new RoundsInput(targets, objects, shapes, triangles, surfaces, terrain, execution), settings);

    /// <returns>The pass's proposals.</returns>
    private FollowUpPass RecordPass(DecidedPass pass)
    {
        _passes.Add(pass);
        return new FollowUpPass([.. pass.RoundProposals.Select(round => round.Proposals)], Failure: null);
    }

    private FollowUpPass StopFollowUps(CaughtFailure failure)
    {
        _failure = PartFailure.OfWholePart(RestingObjectsParts.FollowUp, failure);
        return new FollowUpPass([], _failure);
    }

    private TouchChainSet FindTouchChains(IRemovalDecisions final, ImmutableArray<RoundDetails> evidence) =>
        TouchChains.Find([.. _passes.Select(pass => new TouchPass(pass.Seeds, pass.Rounds))], final, _rule?.Search, evidence, targets.Length, execution);

    /// <returns>The rounds of <paramref name="decided"/> that took a seed from its place, in round order.</returns>
    private static ImmutableArray<Round> SeedRoundsOf(IRemovalDecisions decided, ImmutableArray<TargetId> seeds)
    {
        var seedSet = seeds.ToHashSet();
        return [.. decided.Rounds.Where(round => decided.LeftIn(round).Any(seedSet.Contains))];
    }

    /// <param name="SeedRounds">The rounds that took the seeds from their place, in round order.</param>
    /// <param name="Rounds">The pass's rounds as the decisions number them, in round order.</param>
    /// <param name="RoundProposals">One per round of <paramref name="Rounds"/>.</param>
    /// <param name="Time">The time of the pass's rounds alone.</param>
    private sealed record DecidedPass(
        ImmutableArray<TargetId> Seeds,
        ImmutableArray<Round> SeedRounds,
        ImmutableArray<Round> Rounds,
        ImmutableArray<RoundProposals<RoundDetails>> RoundProposals,
        TimeSpan Time);
}
