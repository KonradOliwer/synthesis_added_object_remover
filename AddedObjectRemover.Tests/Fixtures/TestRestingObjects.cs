using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Runs an also-remove plan the way the runner does: the plan's rule is applied to the decisions.</summary>
internal static class TestRestingObjects
{
    public static RestingObjectsRun Run(RemovalDecisions afterTooClose, RestingObjectsInput input, AlsoRemoveSettings options) =>
        Run(afterTooClose, RestingObjects.Prepare(afterTooClose, input, options));

    public static RestingObjectsRun Run(RemovalDecisions afterTooClose, RestingObjectsPlan plan)
    {
        var (decisions, evidence) = plan.Rounds is { } rounds
            ? afterTooClose.ApplyRoundsUntilNothingRemoved(rounds)
            : (afterTooClose, ImmutableArray<RoundDetails>.Empty);
        return RestingObjects.Finish(plan, afterTooClose, decisions, evidence);
    }
}
