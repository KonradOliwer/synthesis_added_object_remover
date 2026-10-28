using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

public static class VisibleLeftBehindObjects
{
    /// <param name="decisions">The newest decisions; only the removals already in them count as cleared.</param>
    /// <param name="surroundings">The visible target objects, removed or not.</param>
    /// <param name="settings">Null when the step is switched off.</param>
    /// <returns>
    /// The evaluations, and the proposals. An object the round keeps as referenced is already <see cref="VisibleLeftBehindOutcome.KeptReferenced"/>
    /// with its keep reason, and one the round removes as a linked member is <see cref="VisibleLeftBehindOutcome.RemovedLinkedToRemoval"/>: the step decides its round on a working copy of <paramref name="decisions"/> and joins the verdicts itself.
    /// When <paramref name="settings"/> is null the result is <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step:
    /// <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no proposals.
    /// </returns>
    public static VisibleLeftBehindResult Find(
        ImmutableArray<TargetObject> targets,
        IRemovalDecisions decisions,
        IVisibleTargetObjects surroundings,
        IBaseFacts bases,
        IBaseObjectShapes shapes,
        WorkOrder order,
        Execution execution,
        VisibleLeftBehindSettings? settings)
    {
        if (settings is null) return VisibleLeftBehindResult.SwitchedOff;

        return Failures.Attempt(
            () => JoinVerdicts(
                new VisibleLeftBehindRule(targets, shapes, surroundings, bases, settings, order)
                    .SelectRemovals(decisions, execution),
                decisions),
            failure => new VisibleLeftBehindResult(
                StepOutcome.StoppedByUnexpectedError, [], [], PartFailure.OfWholePart(VisibleLeftBehindParts.VisibleLeftBehind, failure)));
    }

    /// <returns>
    /// The result with the round decided on a copy of the decisions: an object the round kept is kept as referenced, and one it removed
    /// as a linked group member is removed as linked.
    /// </returns>
    private static VisibleLeftBehindResult JoinVerdicts(VisibleLeftBehindResult evaluated, IRemovalDecisions decisions)
    {
        var decided = decisions.DecideRoundOnCopy(RoundKind.VisibleLeftBehind, [.. evaluated.Proposals]);
        return evaluated with
        {
            Evaluations =
            [
                .. evaluated.Evaluations.Select(evaluation =>
                    decided.GetValueOrDefault(new TargetId(evaluation.TargetIndex)) switch
                    {
                        Decision.Kept kept => evaluation with { Decision = VisibleLeftBehindOutcome.KeptReferenced, KeepReason = kept.KeepReason },
                        Decision.Removed { Reason: RemovalReason.LinkedTo } => evaluation with { Decision = VisibleLeftBehindOutcome.RemovedLinkedToRemoval },
                        _ => evaluation,
                    }),
            ],
        };
    }
}
