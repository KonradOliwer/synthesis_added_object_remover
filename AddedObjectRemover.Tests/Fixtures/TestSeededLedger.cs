using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A ledger seeded by a too-close round like the pipeline does, and the follow-up rounds a cascade added to it.</summary>
internal static class TestSeededLedger
{
    private static readonly Cause SeedCause = new Cause.TooClose(new OtherId(0));

    public static Ledger Seed(Protection protection, int targetCount, IReadOnlyList<int> seeds) =>
        Ledger.Start(protection, targetCount).Apply(RoundKind.TooClose, [.. seeds.Select(seed => new Proposal(new TargetId(seed), SeedCause))]);

    public static ObjectVisibility[] AllVisible(int targetCount) =>
        Enumerable.Repeat(ObjectVisibility.Visible, targetCount).ToArray();

    public static ImmutableArray<Round> FollowUpRounds(Ledger seeded, Ledger cascaded) => cascaded.Rounds.RemoveRange(0, seeded.Rounds.Length);

    /// <summary>Only the too-close round's causes read the world, and the report is asked for follow-up rounds only.</summary>
    public static LedgerReport ReportOfFollowUpRounds(Ledger ledger) => new(ledger, null!, LeftoverResult.None);
}
