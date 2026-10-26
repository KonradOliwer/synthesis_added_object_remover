namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Runs an also-remove plan the way the step does: the plan's rounds are decided on a working copy of the decisions.</summary>
internal static class TestRestingObjects
{
    public static RestingObjectsResult SwitchedOff { get; } = new(
        StepOutcome.SwitchedOff,
        FollowUpRemovalMode.Nothing,
        HadSeeds: false,
        SeedRounds: [],
        Rounds: [],
        ProposedRounds: [],
        Evidence: [],
        new RestingObjectsWork(0, 0, 0, 0, PairTestStats.Zero),
        RoundsTime: TimeSpan.Zero,
        TouchChains: null,
        Failure: null,
        TouchChainsFailure: null,
        ReportFileDetails.SwitchedOff);

    public static RestingObjectsRun Run(RemovalDecisions afterTooClose, RoundsInput input, AlsoRemoveSettings settings) =>
        Run(afterTooClose, RestingObjects.Prepare(afterTooClose, afterTooClose.Rounds, input, settings));

    public static RestingObjectsRun Run(RemovalDecisions afterTooClose, RestingObjectsPlan plan) =>
        RestingObjects.DecideOnWorkingCopy(plan, afterTooClose, afterTooClose.Rounds);
}
