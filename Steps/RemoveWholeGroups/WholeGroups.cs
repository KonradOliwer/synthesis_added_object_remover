using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups;

public static class WholeGroups
{
    /// <param name="Targets">Every target, by id.</param>
    /// <param name="Decisions">The newest decisions, which the step's round and its follow-up are added to; every object they took from its place seeds.</param>
    /// <param name="Shapes">The boxes the objects are grouped by.</param>
    /// <param name="KindsAlwaysKept">The kinds each left-behind step's settings always keep, also while that step is switched off.</param>
    /// <param name="RoundJoin">Keeps a protected kind and joins the round's decision into a verdict, as the left-behind steps do.</param>
    /// <param name="FollowUp">Follows up the removals of the step's round; no pass ran yet.</param>
    /// <param name="Settings">Null when the step is switched off.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IRunDecisions Decisions,
        IBaseObjectShapes Shapes,
        IReadOnlyList<IKindsAlwaysKept> KindsAlwaysKept,
        ILeftBehindRoundJoin RoundJoin,
        IAlsoRemoveFollowUp FollowUp,
        Execution Execution,
        WholeGroupsSettings? Settings);

    public static Decided<WholeGroupsResult> Find(Input input)
    {
        var result = Evaluate(input);
        return new Decided<WholeGroupsResult>(result, input.Decisions.ApplyRounds(result.ProposedRounds));
    }

    /// <returns>
    /// The seeded groups, every standing candidate in them judged once, the step's one round with its follow-up, and the follow-up's
    /// summary. When the settings are null the result is <see cref="StepOutcome.SwitchedOff"/>. An unexpected failure stops the step:
    /// <see cref="StepOutcome.StoppedByUnexpectedError"/>, with the failure and no rounds.
    /// </returns>
    private static WholeGroupsResult Evaluate(Input input)
    {
        // Taken before the follow-up: a step that stops proposes no round, so it must not report a pass it ran.
        var nothingFollowedUp = input.FollowUp.Summarize(input.Decisions);
        if (input.Settings is not { } settings) return new WholeGroupsResult(StepOutcome.SwitchedOff, [], [], [], nothingFollowedUp, Failure: null);

        return Failures.Attempt(
            () => WholeGroupRound.Decide(WholeGroupRule.Create(input, settings), input),
            failure => new WholeGroupsResult(
                StepOutcome.StoppedByUnexpectedError, [], [], [], nothingFollowedUp, PartFailure.OfWholePart(WholeGroupsParts.WholeGroups, failure)));
    }
}
