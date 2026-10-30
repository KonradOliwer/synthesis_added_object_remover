using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

public static class VisibleLeftBehindObjects
{
    /// <param name="decisions">The newest decisions; the removals in them, and those of the step's earlier rounds and their follow-ups, count as cleared.</param>
    /// <param name="surroundings">The visible target objects, removed or not, where the rounds measure them: a restored one where the cleaned mod put it.</param>
    /// <param name="followUp">Follows up the removals of each round that took something from its place before the next round; no pass ran yet.</param>
    /// <param name="timeSinceStart">The run's clock, which measures the visible rounds.</param>
    /// <param name="settings">Null when the step is switched off.</param>
    /// <returns>
    /// The evaluations, the proposed rounds, the visible rounds as the decisions number them once the proposed rounds are applied, and
    /// the follow-up's summary. Each evaluation is the object's newest check joined with the decision a visible round made: an object a
    /// round keeps as referenced is <see cref="VisibleLeftBehindOutcome.KeptReferenced"/> with its keep reason, and one a round removes
    /// as a linked member is <see cref="VisibleLeftBehindOutcome.RemovedLinkedToRemoval"/> or <see cref="VisibleLeftBehindOutcome.RemovedLinkedToRestoration"/>:
    /// the step decides its rounds on a working copy of <paramref name="decisions"/> and joins the verdicts itself.
    /// When <paramref name="settings"/> is null the result is <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step:
    /// <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no proposed rounds. A step that proposes nothing carries
    /// the follow-up's summary without rounds.
    /// </returns>
    public static VisibleLeftBehindResult Find(
        ImmutableArray<TargetObject> targets,
        IRemovalDecisions decisions,
        IVisibleTargetObjects surroundings,
        IBaseFacts bases,
        IBaseObjectShapes shapes,
        IAlsoRemoveFollowUp followUp,
        Func<TimeSpan> timeSinceStart,
        WorkOrder order,
        Execution execution,
        VisibleLeftBehindSettings? settings)
    {
        // Taken before the first pass: a step that stops proposes no round, so it must not report the passes it ran.
        var nothingFollowedUp = followUp.Summarize(decisions);
        if (settings is null) return new VisibleLeftBehindResult(StepOutcome.SwitchedOff, [], [], [], TimeSpan.Zero, nothingFollowedUp, Failure: null);

        return Failures.Attempt(
            () => JoinVerdicts(DecideRounds(targets, decisions, surroundings, bases, shapes, followUp, timeSinceStart, order, execution, settings), decisions, followUp),
            failure => new VisibleLeftBehindResult(
                StepOutcome.StoppedByUnexpectedError, [], [], [], TimeSpan.Zero, nothingFollowedUp, PartFailure.OfWholePart(VisibleLeftBehindParts.VisibleLeftBehind, failure)));
    }

    private static VisibleLeftBehindRounds DecideRounds(
        ImmutableArray<TargetObject> targets,
        IRemovalDecisions decisions,
        IVisibleTargetObjects surroundings,
        IBaseFacts bases,
        IBaseObjectShapes shapes,
        IAlsoRemoveFollowUp followUp,
        Func<TimeSpan> timeSinceStart,
        WorkOrder order,
        Execution execution,
        VisibleLeftBehindSettings settings)
    {
        var rule = new VisibleLeftBehindRule(targets, shapes, bases, settings);
        var candidatesSurroundings = VisibleObjectSurroundings.Measure(
            targets, rule.Candidates(decisions), surroundings, settings.LookAround, settings.DirectionClearedPercent, order, execution);
        return VisibleLeftBehindRounds.Decide(rule, candidatesSurroundings, followUp, timeSinceStart, decisions);
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
            [.. rounds.LatestChecks.Select(check => Join(check, VisibleRoundDecisionOf(new TargetId(check.TargetIndex), decided), decided))],
            rounds.ProposedRounds,
            [.. decided.RoundsAfter(decisions).Where(round => round.Kind == RoundKind.VisibleLeftBehind)],
            rounds.RoundsTime,
            followUp.Summarize(decided),
            Failure: null);
    }

    /// <remarks>A candidate is undecided before the step, so any decision of it was made by a visible round or a follow-up round of the step.</remarks>
    private static Decision? VisibleRoundDecisionOf(TargetId target, IRemovalDecisions decided) =>
        decided.Of(target) is { Round.Kind: RoundKind.VisibleLeftBehind } decision ? decision : null;

    private static VisibleLeftBehindCheck Join(VisibleLeftBehindCheck evaluation, Decision? decision, IRemovalDecisions decided)
    {
        var effect = RoundEffects.Of(decision);
        return effect switch
        {
            { Outcome: RoundOutcome.KeptAsReferenced } => evaluation with { Decision = VisibleLeftBehindOutcome.KeptReferenced, KeepReason = effect.KeepReason },
            { Outcome: RoundOutcome.Removed, AsLinked: true } => evaluation with { Decision = RemovedAsLinked(decision, decided) },
            { Outcome: RoundOutcome.Untouched or RoundOutcome.Removed } => evaluation,
            { Outcome: RoundOutcome.Restored, AsLinked: true } => evaluation with { Decision = VisibleLeftBehindOutcome.RestoredAsLinked },
            { Outcome: RoundOutcome.Restored } => evaluation with { Decision = VisibleLeftBehindOutcomes.AsRestored(evaluation.Decision) },
            _ => throw new UnreachableException($"Unexpected round effect {effect} in a left-behind round."),
        };
    }

    private static VisibleLeftBehindOutcome RemovedAsLinked(Decision? decision, IRemovalDecisions decided) =>
        RoundEffects.FollowedARestoredMember(decision, decided.Of) ? VisibleLeftBehindOutcome.RemovedLinkedToRestoration : VisibleLeftBehindOutcome.RemovedLinkedToRemoval;
}
