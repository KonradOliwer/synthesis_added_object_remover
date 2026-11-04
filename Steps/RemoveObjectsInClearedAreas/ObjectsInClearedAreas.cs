using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;

public static class ObjectsInClearedAreas
{
    /// <param name="Targets">Every target, by id.</param>
    /// <param name="Spaces">Every space the scan read.</param>
    /// <param name="Decisions">The decisions the areas are judged on.</param>
    /// <param name="Caches">Index the shown targets where the rounds measure them; their shapes are read.</param>
    public sealed record ClearedAreasInput(
        ImmutableArray<TargetObject> Targets,
        IReadOnlyDictionary<RecordKey, SpaceFact> Spaces,
        IRemovalDecisions Decisions,
        IRunCaches Caches,
        ClearedArea Settings);

    /// <param name="Decisions">The newest decisions, which the step's round and its follow-up are added to; the areas are judged on them.</param>
    /// <param name="Caches">Index the shown targets where the rounds measure them; their shapes are read.</param>
    /// <param name="KindsAlwaysKept">The kinds each left-behind step's settings always keep, also while that step is switched off.</param>
    /// <param name="RoundJoin">Keeps a protected kind and joins the round's decision into a verdict, as both left-behind steps do.</param>
    /// <param name="FollowUp">Follows up the removals of the step's round; no pass ran yet.</param>
    /// <param name="TimeSinceStart">The run's clock, which times the judging.</param>
    /// <param name="Settings">Null when the step is switched off.</param>
    public sealed record Input(
        ImmutableArray<TargetObject> Targets,
        IReadOnlyDictionary<RecordKey, SpaceFact> Spaces,
        IRunDecisions Decisions,
        IRunCaches Caches,
        IReadOnlyList<IKindsAlwaysKept> KindsAlwaysKept,
        ILeftBehindRoundJoin RoundJoin,
        IAlsoRemoveFollowUp FollowUp,
        Func<TimeSpan> TimeSinceStart,
        WorkOrder Order,
        Execution Execution,
        ObjectsInClearedAreasSettings? Settings);

    /// <summary>The rule that judges how much of the cleaned mod's drawn objects still stand around a point, on the given decisions.</summary>
    public static IClearedAreas CreateClearedAreas(ClearedAreasInput input) =>
        BuildJudge(input.Targets, input.Decisions.TargetsAsTheRoundsMeasureThem(input.Targets), input.Spaces, input.Decisions, input.Caches, input.Settings);

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
        var (targets, decisions, caches) = (input.Targets, input.Decisions, input.Caches);
        var measured = decisions.TargetsAsTheRoundsMeasureThem(targets);
        var areas = BuildJudge(targets, measured, input.Spaces, decisions, caches, settings.ClearedArea);
        return new ClearedAreaRule(measured, caches.Shapes, areas, input.KindsAlwaysKept, input.RoundJoin, settings.MinObjectsAround);
    }

    private static ClearedAreaJudge BuildJudge(
        ImmutableArray<TargetObject> targets,
        ImmutableArray<TargetObject> measured,
        IReadOnlyDictionary<RecordKey, SpaceFact> spaces,
        IRemovalDecisions decisions,
        IRunCaches caches,
        ClearedArea settings)
    {
        var shownTargets = caches.IndexShownTargets(measured);
        return new ClearedAreaJudge(new AreaContents(targets, shownTargets, caches.Shapes), decisions, spaces, settings);
    }
}
