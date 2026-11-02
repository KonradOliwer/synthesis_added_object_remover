using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

public static class LeftBehindObjects
{
    /// <param name="OtherModObjects">The other-mod objects that can cause removals (the selection step's view).</param>
    /// <param name="OtherModsObjects">What became of the other mods' objects: no object sits inside one the run removes or pulls down any more.</param>
    /// <param name="Decisions">The newest decisions, which the step's round is added to.</param>
    /// <param name="Caches">
    /// Index the visible target objects, removed or not, where the rounds measure them: a restored one where the cleaned mod put it;
    /// their base facts and shapes are read.
    /// </param>
    /// <param name="Judging">Judges an object by its surroundings and joins a round's decision into its verdict.</param>
    /// <param name="Settings">Null when the step is switched off.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IObjectsThatCanCauseRemovals OtherModObjects,
        OtherModsObjectsResult OtherModsObjects,
        IRunDecisions Decisions,
        IRunCaches Caches,
        ILeftBehindJudging Judging,
        WorkOrder Order,
        Execution Execution,
        LeftBehindSettings? Settings);

    public static Decided<LeftBehindResult> Find(Input input)
    {
        var result = Evaluate(input);
        return new Decided<LeftBehindResult>(result, input.Decisions.Apply(RoundKind.LeftBehind, result.Proposals));
    }

    /// <returns>
    /// The evaluations, and the proposals; only the removals already in the decisions count as cleared. An object the round keeps as referenced is already <see cref="LeftBehindOutcome.KeptReferenced"/>
    /// with its keep reason (<see cref="LeftBehindOutcome.KeptInDeletedCell"/> when the reason is a deleted cell), and one the round removes as a linked member is <see cref="LeftBehindOutcome.RemovedLinkedToRemoval"/> or <see cref="LeftBehindOutcome.RemovedLinkedToRestoration"/>: the step decides its round on a working copy of the decisions and joins the verdicts itself.
    /// When the settings are null the step does not look for containing objects, makes no copy, and the result is
    /// <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>,
    /// with the failure and no proposals.
    /// </returns>
    private static LeftBehindResult Evaluate(Input input)
    {
        if (input.Settings is not { } settings) return LeftBehindResult.SwitchedOff;

        var (targets, shapes, order, execution) = (input.Targets, input.Caches.Shapes, input.Order, input.Execution);
        var takenAway = input.OtherModsObjects.TakenAway().Keys.ToHashSet();
        var surroundings = input.Caches.IndexVisibleTargets(input.Decisions.TargetsAsTheRoundsMeasureThem(targets));
        return Failures.Attempt(
            () => JoinVerdicts(
                new LeftBehindRule(
                        targets,
                        shapes,
                        surroundings,
                        ContainingObjectFinder.Find(targets, shapes, input.OtherModObjects, takenAway, order, execution),
                        new ReachRule(input.Caches.Bases),
                        input.Judging,
                        settings,
                        order)
                    .SelectRemovals(input.Decisions, execution),
                input.Decisions,
                input.Judging),
            failure => new LeftBehindResult(StepOutcome.StoppedByUnexpectedError, [], [], PartFailure.OfWholePart(LeftBehindParts.LeftBehind, failure)));
    }

    /// <returns>
    /// The result with the round decided on a copy of the decisions: an object the round kept is kept as referenced, and one it removed
    /// as a linked group member is removed as linked.
    /// </returns>
    private static LeftBehindResult JoinVerdicts(LeftBehindResult evaluated, IRemovalDecisions decisions, ILeftBehindJudging judging)
    {
        var decided = decisions.DecideRoundOnCopy(RoundKind.LeftBehind, [.. evaluated.Proposals]);
        Func<TargetId, Decision?> decisionOf = target => decided.GetValueOrDefault(target);
        return evaluated with
        {
            Evaluations =
            [
                .. evaluated.Evaluations.Select(evaluation =>
                    judging.JoinRoundDecision(evaluation, decisionOf(new TargetId(evaluation.TargetIndex)), decisionOf)),
            ],
        };
    }
}
