using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;

public static class ObjectsInClearedAreas
{
    /// <param name="Targets">Every target, by id.</param>
    /// <param name="Decisions">The newest decisions, which the step's round and its follow-up are added to; the areas are judged on them.</param>
    /// <param name="Caches">Their shapes are read.</param>
    /// <param name="Areas">Judges the areas around the targets on the decisions.</param>
    /// <param name="KindsAlwaysKept">The kinds each left-behind step's settings always keep, also while that step is switched off.</param>
    /// <param name="RoundJoin">Keeps a protected kind and joins the round's decision into a verdict, as both left-behind steps do.</param>
    /// <param name="FollowUp">Follows up the removals of the step's round; no pass ran yet.</param>
    /// <param name="TimeSinceStart">The run's clock, which times the judging.</param>
    /// <param name="Settings">Null when the step is switched off.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IRunDecisions Decisions,
        IRunCaches Caches,
        IClearedAreasJudging Areas,
        IReadOnlyList<IKindsAlwaysKept> KindsAlwaysKept,
        ILeftBehindRoundJoin RoundJoin,
        IAlsoRemoveFollowUp FollowUp,
        Func<TimeSpan> TimeSinceStart,
        WorkOrder Order,
        Execution Execution,
        ObjectsInClearedAreasSettings? Settings);

    public static Decided<ObjectsInClearedAreasResult> Find(Input input)
    {
        var result = Evaluate(input);
        return new Decided<ObjectsInClearedAreasResult>(result, input.Decisions.ApplyRounds(result.ProposedRounds));
    }

    /// <returns>
    /// Every candidate judged once on the decisions of the earlier steps, the step's one round with its follow-up, and the follow-up's
    /// summary; the step decides its round on a working copy of the decisions and joins the verdicts itself. When the settings are null
    /// the result is <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step:
    /// <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no rounds.
    /// </returns>
    private static ObjectsInClearedAreasResult Evaluate(Input input)
    {
        // Taken before the follow-up: a step that stops proposes no round, so it must not report a pass it ran.
        var nothingFollowedUp = input.FollowUp.Summarize(input.Decisions);
        if (input.Settings is not { } settings) return new ObjectsInClearedAreasResult(StepOutcome.SwitchedOff, [], [], TimeSpan.Zero, nothingFollowedUp, Failure: null);

        return Failures.Attempt(
            () => ClearedAreaRound.Decide(CreateRule(input, settings), input),
            failure => new ObjectsInClearedAreasResult(
                StepOutcome.StoppedByUnexpectedError, [], [], TimeSpan.Zero, nothingFollowedUp, PartFailure.OfWholePart(ObjectsInClearedAreasParts.ObjectsInClearedAreas, failure)));
    }

    private static ClearedAreaRule CreateRule(Input input, ObjectsInClearedAreasSettings settings)
    {
        var decisions = input.Decisions;
        var areas = input.Areas.JudgedOn(decisions, settings.ClearedArea);
        return new ClearedAreaRule(
            decisions.TargetsAsTheRoundsMeasureThem(input.Targets), input.Caches.Shapes, areas, input.KindsAlwaysKept, input.RoundJoin, settings.MinObjectsAround);
    }
}
