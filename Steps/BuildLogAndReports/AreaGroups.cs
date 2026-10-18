using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>A worldspace, hold or place of one space, as the area lines name it.</summary>
internal readonly record struct AreaGroup(string Name, RecordKey Space);

/// <summary>Target objects counted by where they are.</summary>
internal static class AreaGroups
{
    /// <summary>By name (ordinal), then by space key.</summary>
    private static readonly IComparer<AreaGroup> NameThenSpace = Comparer<AreaGroup>.Create((first, second) =>
    {
        var byName = string.CompareOrdinal(first.Name, second.Name);
        return byName != 0 ? byName : RecordKeyOrder.Comparer.Compare(first.Space, second.Space);
    });

    /// <returns>The objects per area, most first; equal counts by name (ordinal), then by space key.</returns>
    public static List<KeyValuePair<AreaGroup, int>> Rank(CollectedObjects world, IEnumerable<int> targetIndices, AreaLevel level) =>
        KeyedGroups.Rank(KeyedGroups.CountBy(targetIndices, index => Of(world, world.Targets[index], level), EqualityComparer<AreaGroup>.Default), NameThenSpace);

    /// <summary>A hold or place is named with its worldspace, so equal names in different spaces read apart.</summary>
    public static AreaGroup Of(CollectedObjects world, TargetObject target, AreaLevel level)
    {
        var space = world.Spaces[target.SpaceKey];
        var name = AreaText.Name(level, target.Area, space);
        return new AreaGroup(level == AreaLevel.Worldspace ? name : AreaText.WithWorldspace(name, space), target.SpaceKey);
    }
}
