using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

public static class VisibleLeftBehindObjects
{
    /// <summary>The direction rule this step judges its objects by (see <see cref="SurroundingsJudging"/>).</summary>
    public static ISurroundingsJudging CreateSurroundingsJudging() => new SurroundingsJudging();

    /// <summary>The rules this step joins a round's decisions into its verdicts by (see <see cref="LeftBehindRoundJoin"/>).</summary>
    public static ILeftBehindRoundJoin CreateLeftBehindRoundJoin() => new LeftBehindRoundJoin();

    /// <param name="Caches">The base facts and shapes of the objects are read.</param>
    /// <param name="NeverRemove">The kinds the step's settings list, also while the step is switched off.</param>
    public sealed record KindsInput(IRunCaches Caches, NeverRemovedVisibleKinds NeverRemove);

    /// <summary>The visible objects of the kinds the step's settings list (see <see cref="VisibleKindsAlwaysKept"/>).</summary>
    public static IKindsAlwaysKept CreateKindsAlwaysKept(KindsInput input) =>
        new VisibleKindsAlwaysKept(input.Caches.Shapes, input.Caches.Bases, input.NeverRemove.Kinds);

    /// <param name="Decisions">The decisions the neighbours stand in: each visible target where their rounds measure it.</param>
    /// <param name="Caches">Index the visible target objects; their shapes are read.</param>
    /// <param name="Settings">Null when the step is switched off.</param>
    public sealed record NeighboursInput(ImmutableArray<TargetObject> Targets, IRemovalDecisions Decisions, IRunCaches Caches, VisibleLeftBehindSettings? Settings);

    /// <summary>
    /// The neighbours the step measures around a shown target with its look-around distance (see <see cref="VisibleNeighbours"/>);
    /// none while the step is switched off.
    /// </summary>
    public static ILeftBehindNeighbours CreateNeighbours(NeighboursInput input) =>
        new VisibleNeighbours(
            input.Caches.Shapes,
            input.Settings is { } settings
                ? (input.Caches.IndexVisibleTargets(input.Decisions.TargetsAsTheRoundsMeasureThem(input.Targets)), settings.Surroundings.LookAround)
                : null);

    /// <param name="Decisions">The newest decisions, which the step's rounds and their follow-ups are added to.</param>
    /// <param name="Caches">Index the visible target objects where the rounds measure them; their base facts and shapes are read.</param>
    /// <param name="KindsAlwaysKept">The visible kinds the settings list (see <see cref="CreateKindsAlwaysKept"/>).</param>
    /// <param name="FollowUp">Follows up the removals of each round that took something from its place before the next round; no pass ran yet.</param>
    /// <param name="TimeSinceStart">The run's clock, which measures the visible rounds.</param>
    /// <param name="Settings">Null when the step is switched off.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IRunDecisions Decisions,
        IRunCaches Caches,
        IKindsAlwaysKept KindsAlwaysKept,
        IAlsoRemoveFollowUp FollowUp,
        Func<TimeSpan> TimeSinceStart,
        WorkOrder Order,
        Execution Execution,
        VisibleLeftBehindSettings? Settings);

    public static Decided<VisibleLeftBehindResult> Find(Input input)
    {
        var result = FindRounds(input);
        return new Decided<VisibleLeftBehindResult>(result, input.Decisions.ApplyRounds(result.ProposedRounds));
    }

    /// <returns>
    /// The evaluations, the proposed rounds, the visible rounds as the decisions number them once the proposed rounds are applied, and
    /// the follow-up's summary. Each evaluation is the object's newest check joined with the decision a visible round made: an object a
    /// round keeps as referenced is <see cref="LeftBehindOutcome.KeptReferenced"/> with its keep reason (<see cref="LeftBehindOutcome.KeptInDeletedCell"/> when the reason is a deleted cell), and one a round removes
    /// as a linked member is <see cref="LeftBehindOutcome.RemovedLinkedToRemoval"/> or <see cref="LeftBehindOutcome.RemovedLinkedToRestoration"/>:
    /// the step decides its rounds on a working copy of the decisions and joins the verdicts itself; the removals in the decisions, and
    /// those of the step's earlier rounds and their follow-ups, count as cleared.
    /// When the settings are null the result is <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step:
    /// <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no proposed rounds. A step that proposes nothing carries
    /// the follow-up's summary without rounds.
    /// </returns>
    private static VisibleLeftBehindResult FindRounds(Input input)
    {
        var (decisions, followUp) = (input.Decisions, input.FollowUp);
        // Taken before the first pass: a step that stops proposes no round, so it must not report the passes it ran.
        var nothingFollowedUp = followUp.Summarize(decisions);
        if (input.Settings is not { } settings) return new VisibleLeftBehindResult(StepOutcome.SwitchedOff, [], [], [], TimeSpan.Zero, nothingFollowedUp, Failure: null);

        // Effect-only objects are judged but are no surroundings: effect pieces whose solid surroundings are gone would otherwise keep each other.
        var surroundings = input.Caches.IndexVisibleTargets(decisions.TargetsAsTheRoundsMeasureThem(input.Targets));
        return Failures.Attempt(
            () => JoinVerdicts(DecideRounds(input, surroundings, settings), decisions, followUp),
            failure => new VisibleLeftBehindResult(
                StepOutcome.StoppedByUnexpectedError, [], [], [], TimeSpan.Zero, nothingFollowedUp, PartFailure.OfWholePart(VisibleLeftBehindParts.VisibleLeftBehind, failure)));
    }

    /// <param name="surroundings">The visible target objects, removed or not, where the rounds measure them: a restored one where the cleaned mod put it.</param>
    private static VisibleLeftBehindRounds DecideRounds(Input input, IVisibleTargetObjects surroundings, VisibleLeftBehindSettings settings)
    {
        var (targets, decisions) = (input.Targets, input.Decisions);
        var rule = new VisibleLeftBehindRule(targets, input.Caches.Shapes, input.Caches.Bases, settings, input.KindsAlwaysKept);
        var candidatesSurroundings = VisibleObjectSurroundings.Measure(
            targets, rule.Candidates(decisions), surroundings, settings.Surroundings.LookAround, settings.Surroundings.DirectionClearedPercent, input.Order, input.Execution);
        return VisibleLeftBehindRounds.Decide(rule, candidatesSurroundings, input.FollowUp, input.TimeSinceStart, decisions);
    }

    /// <returns>
    /// The result with each candidate's newest check joined with the decision a visible round made: an object a round kept is kept as
    /// referenced, and one it removed as a linked group member is removed as linked.
    /// </returns>
    private static VisibleLeftBehindResult JoinVerdicts(VisibleLeftBehindRounds rounds, IRemovalDecisions decisions, IAlsoRemoveFollowUp followUp)
    {
        var decided = rounds.Decided;
        return new VisibleLeftBehindResult(
            StepOutcome.Performed,
            [.. rounds.LatestChecks.Select(check => LeftBehindVerdicts.JoinRoundDecision(check, VisibleRoundDecisionOf(new TargetId(check.TargetIndex), decided), decided.Of))],
            rounds.ProposedRounds,
            [.. decided.RoundsAfter(decisions).Where(round => round.Kind == RoundKind.VisibleLeftBehind)],
            rounds.RoundsTime,
            followUp.Summarize(decided),
            Failure: null);
    }

    /// <remarks>A candidate is undecided before the step, so any decision of it was made by a visible round or a follow-up round of the step.</remarks>
    private static Decision? VisibleRoundDecisionOf(TargetId target, IRemovalDecisions decided) =>
        decided.Of(target) is { Round.Kind: RoundKind.VisibleLeftBehind } decision ? decision : null;
}
