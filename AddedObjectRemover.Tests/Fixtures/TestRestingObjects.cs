namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Runs an also-remove plan the way the step does: the plan's rounds are decided on a working copy of the decisions.</summary>
internal static class TestRestingObjects
{
    public static RestingObjectsRun Run(RemovalDecisions afterTooClose, RoundsInput input, AlsoRemoveSettings settings) =>
        Run(afterTooClose, RestingObjects.Prepare(afterTooClose, input, settings));

    public static RestingObjectsRun Run(RemovalDecisions afterTooClose, RestingObjectsPlan plan) =>
        RestingObjects.DecideOnWorkingCopy(plan, afterTooClose);
}
