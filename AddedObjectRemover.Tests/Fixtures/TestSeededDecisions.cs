namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Removal decisions seeded by a too-close round like the pipeline does.</summary>
internal static class TestSeededDecisions
{
    private static readonly RemovalReason SeedReason = new RemovalReason.TooClose(new OtherId(0));

    public static RemovalDecisions Seed(ObjectsToKeep protection, int targetCount, IReadOnlyList<int> seeds) =>
        RemovalDecisions.Start(protection, targetCount)
            .Apply(RoundKind.TooClose, [.. seeds.Select(seed => new ProposedRemoval(new TargetId(seed), SeedReason))]);
}
