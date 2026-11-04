namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The too-close step over a world, with the rule's searches over that same world, as the runner builds them.</summary>
internal static class TestTooClose
{
    public static Decided<TooCloseResult> Find(
        TooCloseWorld world,
        CollectedObjects collected,
        WorkOrder order,
        Execution execution,
        IEnumerable<ObjectsJudgedWith<TooCloseSettings>> groups,
        IRunDecisions decisions) =>
        TooCloseObjects.Find(new(world, TooCloseRules.CreateSearches(world), collected, order, execution, groups, decisions));
}
