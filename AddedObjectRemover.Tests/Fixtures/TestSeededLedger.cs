namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A ledger seeded by a too-close round like the pipeline does.</summary>
internal static class TestSeededLedger
{
    private static readonly Cause SeedCause = new Cause.TooClose(new OtherId(0));

    public static Ledger Seed(Protection protection, int targetCount, IReadOnlyList<int> seeds) =>
        Ledger.Start(protection, targetCount).Apply(RoundKind.TooClose, [.. seeds.Select(seed => new Proposal(new TargetId(seed), SeedCause))]);

    public static TargetLooks AllVisible(int targetCount) =>
        new([.. Enumerable.Repeat(ObjectVisibility.Visible, targetCount)]);

}
