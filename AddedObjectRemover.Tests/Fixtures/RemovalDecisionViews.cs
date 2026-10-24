using System.Collections.Immutable;

namespace AddedObjectRemover.Tests;

/// <summary>Views of the removal decisions that the tests read, made of the decisions' own round-by-round views.</summary>
internal static class RemovalDecisionViews
{
    /// <summary>Every removed object, in target order.</summary>
    public static ImmutableArray<TargetId> RemovedTargets(this IRemovalDecisions decisions) =>
        [.. decisions.Rounds.SelectMany(round => decisions.RemovedIn(round)).Order()];
}
