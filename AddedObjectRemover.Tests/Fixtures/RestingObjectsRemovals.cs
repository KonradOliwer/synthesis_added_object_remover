namespace AddedObjectRemover.Tests.Fixtures;

internal static class RestingObjectsRemovals
{
    /// <summary>The also-remove rounds' removals by touch or lost support, without the linked group members removed with them.</summary>
    public static int CountByRule(RestingObjectsResult result) =>
        result.Rounds.Sum(round => result.RemovalDecisions.RemovedIn(round).Count(target => result.RemovalDecisions.Of(target)!.Reason is not RemovalReason.LinkedTo));
}
