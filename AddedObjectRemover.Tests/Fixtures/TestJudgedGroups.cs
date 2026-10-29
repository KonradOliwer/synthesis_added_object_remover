using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Groups of target objects for the steps that judge groups.</summary>
internal static class TestJudgedGroups
{
    public static ObjectsJudgedWith<TSettings> Of<TSettings>(IEnumerable<int> targetIndexes, TSettings settings) =>
        new([.. targetIndexes.Select(index => new TargetId(index))], settings);

    /// <summary>One group of every target.</summary>
    public static ObjectsJudgedWith<TSettings> OfAll<TSettings>(IEnumerable<TargetObject> targets, TSettings settings) => new(IdsOf(targets), settings);

    public static ImmutableArray<TargetId> IdsOf(IEnumerable<TargetObject> targets) => [.. targets.Select(target => target.Id)];
}
