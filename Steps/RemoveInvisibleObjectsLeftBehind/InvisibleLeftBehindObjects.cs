using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

public static class InvisibleLeftBehindObjects
{
    /// <param name="OtherModObjects">The other-mod objects that can cause removals (the selection step's view).</param>
    /// <param name="OtherModsObjects">What became of the other mods' objects: no object sits inside one the run removes or pulls down any more.</param>
    /// <param name="Decisions">The newest decisions, which the step's round is added to.</param>
    /// <param name="Caches">
    /// Index the visible target objects, removed or not, where the rounds measure them: a restored one where the cleaned mod put it;
    /// their base facts and shapes are read.
    /// </param>
    /// <param name="KindsAlwaysKept">The invisible kinds the settings list (see <see cref="CreateKindsAlwaysKept"/>).</param>
    /// <param name="Judging">Judges an object by its surroundings.</param>
    /// <param name="RoundJoin">Joins a round's decision into the verdict of its object.</param>
    /// <param name="Settings">Null when the step is switched off.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IObjectsThatCanCauseRemovals OtherModObjects,
        OtherModsObjectsResult OtherModsObjects,
        IRunDecisions Decisions,
        IRunCaches Caches,
        IKindsAlwaysKept KindsAlwaysKept,
        ISurroundingsJudging Judging,
        ILeftBehindRoundJoin RoundJoin,
        WorkOrder Order,
        Execution Execution,
        InvisibleLeftBehindSettings? Settings);

    /// <param name="Shapes">The kinds of the objects are read.</param>
    /// <param name="NeverRemove">The kinds the step's settings list, also while the step is switched off.</param>
    public sealed record KindsInput(IBaseObjectShapes Shapes, NeverRemovedInvisibleKinds NeverRemove);

    /// <summary>The invisible objects of the kinds the step's settings list (see <see cref="InvisibleKindsAlwaysKept"/>).</summary>
    public static IKindsAlwaysKept CreateKindsAlwaysKept(KindsInput input) => new InvisibleKindsAlwaysKept(input.Shapes, input.NeverRemove.Kinds);

    public static Decided<InvisibleLeftBehindResult> Find(Input input)
    {
        var result = Evaluate(input);
        return new Decided<InvisibleLeftBehindResult>(result, input.Decisions.Apply(RoundKind.InvisibleLeftBehind, result.Proposals));
    }

    /// <returns>
    /// The evaluations, and the proposals; only the removals already in the decisions count as cleared. An object the round keeps as referenced is already <see cref="LeftBehindOutcome.KeptReferenced"/>
    /// with its keep reason (<see cref="LeftBehindOutcome.KeptInDeletedCell"/> when the reason is a deleted cell), and one the round removes as a linked member is <see cref="LeftBehindOutcome.RemovedLinkedToRemoval"/> or <see cref="LeftBehindOutcome.RemovedLinkedToRestoration"/>: the step decides its round on a working copy of the decisions and joins the verdicts itself.
    /// When the settings are null the step does not look for containing objects, makes no copy, and the result is
    /// <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step: <see cref="StepOutcome.StoppedByUnexpectedError"/>,
    /// with the failure and no proposals.
    /// </returns>
    private static InvisibleLeftBehindResult Evaluate(Input input)
    {
        if (input.Settings is not { } settings) return InvisibleLeftBehindResult.SwitchedOff;

        var (targets, shapes, order, execution) = (input.Targets, input.Caches.Shapes, input.Order, input.Execution);
        var takenAway = input.OtherModsObjects.TakenAway().Keys.ToHashSet();
        var surroundings = input.Caches.IndexVisibleTargets(input.Decisions.TargetsAsTheRoundsMeasureThem(targets));
        return Failures.Attempt(
            () => JoinVerdicts(
                new InvisibleLeftBehindRule(
                        targets,
                        shapes,
                        surroundings,
                        ContainingObjectFinder.Find(targets, shapes, input.OtherModObjects, takenAway, order, execution),
                        new ReachRule(input.Caches.Bases),
                        input.Judging,
                        input.RoundJoin,
                        settings,
                        input.KindsAlwaysKept,
                        order)
                    .SelectRemovals(input.Decisions, execution),
                input.Decisions,
                input.RoundJoin),
            failure => new InvisibleLeftBehindResult(StepOutcome.StoppedByUnexpectedError, [], [], PartFailure.OfWholePart(InvisibleLeftBehindParts.LeftBehind, failure)));
    }

    /// <returns>
    /// The result with the round decided on a copy of the decisions: an object the round kept is kept as referenced, and one it removed
    /// as a linked group member is removed as linked.
    /// </returns>
    private static InvisibleLeftBehindResult JoinVerdicts(InvisibleLeftBehindResult evaluated, IRemovalDecisions decisions, ILeftBehindRoundJoin roundJoin)
    {
        var decided = decisions.DecideRoundOnCopy(RoundKind.InvisibleLeftBehind, [.. evaluated.Proposals]);
        Func<TargetId, Decision?> decisionOf = target => decided.GetValueOrDefault(target);
        return evaluated with
        {
            Evaluations =
            [
                .. evaluated.Evaluations.Select(evaluation =>
                    roundJoin.JoinRoundDecision(evaluation, decisionOf(new TargetId(evaluation.TargetIndex)), decisionOf)),
            ],
        };
    }
}
