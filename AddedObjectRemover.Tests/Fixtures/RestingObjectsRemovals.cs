namespace AddedObjectRemover.Tests.Fixtures;

internal static class RestingObjectsRemovals
{
    /// <summary>The also-remove rounds' removals by touch or lost support, without the linked group members removed with them.</summary>
    /// <param name="decisions">Decisions that hold the also-remove rounds.</param>
    public static int CountByRule(RestingObjectsResult result, IRemovalDecisions decisions) =>
        result.Rounds.Sum(round => decisions.RemovedIn(round).Count(target => decisions.Of(target)!.Reason is not RemovalReason.LinkedTo));

    /// <param name="decisions">Decisions that hold the also-remove rounds.</param>
    public static int CountKept(RestingObjectsResult result, IRemovalDecisions decisions) => result.Rounds.Sum(round => decisions.KeptIn(round).Length);
}
