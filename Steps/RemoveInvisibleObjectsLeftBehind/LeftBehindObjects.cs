using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

public static class LeftBehindObjects
{
    /// <param name="otherModObjects">The other-mod objects that can cause removals (the selection step's view).</param>
    /// <param name="otherModObjectsTakenAway">Those of any plugin this run removes or pulls down: no object sits inside them any more.</param>
    /// <param name="decisions">The newest decisions; only the removals already in them count as cleared.</param>
    /// <param name="surroundings">The visible target objects, removed or not.</param>
    /// <param name="settings">Null when the step is switched off.</param>
    /// <returns>
    /// The evaluations, and the proposals. An object the round keeps as referenced is already <see cref="LeftBehindOutcome.KeptReferenced"/>
    /// with its keep reason: the step decides its round on a working copy of <paramref name="decisions"/> and joins the verdicts itself.
    /// When <paramref name="settings"/> is null the step does not look for containing objects, makes no copy, and the result is
    /// <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>,
    /// with the failure and no proposals.
    /// </returns>
    public static LeftBehindResult Find(
        ImmutableArray<TargetObject> targets,
        IObjectsThatCanCauseRemovals otherModObjects,
        IReadOnlySet<OtherId> otherModObjectsTakenAway,
        IRemovalDecisions decisions,
        IVisibleTargetObjects surroundings,
        IBaseFacts bases,
        IBaseObjectShapes shapes,
        WorkOrder order,
        Execution execution,
        LeftBehindSettings? settings)
    {
        if (settings is null) return LeftBehindResult.SwitchedOff;

        return Failures.Attempt(
            () => JoinVerdicts(
                new LeftBehindRule(
                        targets,
                        shapes,
                        surroundings,
                        ContainingObjectFinder.Find(targets, shapes, otherModObjects, otherModObjectsTakenAway, order, execution),
                        new ReachRule(bases),
                        settings,
                        order)
                    .SelectRemovals(decisions, execution),
                decisions),
            failure => new LeftBehindResult(StepOutcome.StoppedByUnexpectedError, [], [], PartFailure.OfWholePart(LeftBehindParts.LeftBehind, failure)));
    }

    /// <returns>
    /// The result with the round decided on a copy of the decisions: an object the round kept is kept as referenced, and one it removed
    /// as a linked group member is removed as linked.
    /// </returns>
    private static LeftBehindResult JoinVerdicts(LeftBehindResult evaluated, IRemovalDecisions decisions)
    {
        var decided = decisions.DecideRoundOnCopy(RoundKind.LeftBehind, [.. evaluated.Proposals]);
        return evaluated with
        {
            Evaluations =
            [
                .. evaluated.Evaluations.Select(evaluation =>
                    decided.GetValueOrDefault(new TargetId(evaluation.TargetIndex)) switch
                    {
                        Decision.Kept kept => evaluation with { Decision = LeftBehindOutcome.KeptReferenced, KeepReason = kept.KeepReason },
                        Decision.Removed { Reason: RemovalReason.LinkedTo } => evaluation with { Decision = LeftBehindOutcome.RemovedLinkedToRemoval },
                        _ => evaluation,
                    }),
            ],
        };
    }
}
