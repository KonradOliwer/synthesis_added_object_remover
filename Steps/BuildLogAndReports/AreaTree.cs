using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>A worldspace, hold or place of the area tree, with how many listed and how many target objects are there.</summary>
/// <param name="Children">The holds of a worldspace or the places of a hold, most listed objects first; none for a place.</param>
internal sealed record AreaNode(string Name, int Count, int TargetsThere, IReadOnlyList<AreaNode> Children);

/// <summary>One area of one level: the space and its name, with the hold and place below it as far as the level goes.</summary>
internal readonly record struct AreaKey(RecordKey Space, string Worldspace, string? Hold, string? Place)
{
    public string Name => Place ?? Hold ?? Worldspace;
}

/// <summary>Where one target object is, at every level.</summary>
internal readonly record struct AreaPath(RecordKey Space, string Worldspace, string Hold, string Place)
{
    public static AreaPath Of(TargetObject target, SpaceFact space) =>
        new(target.SpaceKey, AreaText.Worldspace(space), AreaText.HoldLabel(target.Area), AreaText.Place(target.Area, space));

    public AreaKey KeyAt(AreaLevel level) => level switch
    {
        AreaLevel.Worldspace => new AreaKey(Space, Worldspace, null, null),
        AreaLevel.Hold => new AreaKey(Space, Worldspace, Hold, null),
        AreaLevel.Place => new AreaKey(Space, Worldspace, Hold, Place),
        _ => throw new UnreachableException($"Unknown area level {level}."),
    };
}

/// <summary>Target objects grouped by worldspace, then hold, then place. Built once per log from all target objects and only read.</summary>
/// <param name="Paths">Where each target object is, by target index.</param>
/// <param name="TargetsThere">How many target objects each area of every level has.</param>
internal sealed record AreaTree(ImmutableArray<AreaPath> Paths, IReadOnlyDictionary<AreaKey, int> TargetsThere)
{
    /// <summary>By name (ordinal), then by space key, so equal names in different spaces keep one order.</summary>
    private static readonly IComparer<AreaKey> NameThenSpace = Comparer<AreaKey>.Create((first, second) =>
    {
        var byName = string.CompareOrdinal(first.Name, second.Name);
        return byName != 0 ? byName : RecordKeyOrder.Comparer.Compare(first.Space, second.Space);
    });

    public static AreaTree Of(CollectedObjects world)
    {
        ImmutableArray<AreaPath> paths = [.. world.Targets.Select(target => AreaPath.Of(target, world.Spaces[target.SpaceKey]))];
        var areas = Enum.GetValues<AreaLevel>().SelectMany(level => paths.Select(path => path.KeyAt(level)));
        return new AreaTree(paths, KeyedGroups.CountBy(areas, area => area, EqualityComparer<AreaKey>.Default).ToDictionary());
    }

    /// <returns>The worldspaces of the listed target objects; siblings by count (most first), then name (ordinal), then space key.</returns>
    public IReadOnlyList<AreaNode> NodesOf(IEnumerable<int> targetIndices) => NodesAt(AreaLevel.Worldspace, [.. targetIndices.Select(index => Paths[index])]);

    private List<AreaNode> NodesAt(AreaLevel level, IReadOnlyList<AreaPath> listed) =>
        [
            .. KeyedGroups.Rank(KeyedGroups.CountBy(listed, path => path.KeyAt(level), EqualityComparer<AreaKey>.Default), NameThenSpace)
                .Select(area => CreateNode(level, area, listed)),
        ];

    private AreaNode CreateNode(AreaLevel level, KeyValuePair<AreaKey, int> area, IReadOnlyList<AreaPath> listed) =>
        new(
            area.Key.Name,
            area.Value,
            TargetsThere[area.Key],
            level == AreaLevel.Place ? [] : NodesAt(level + 1, [.. listed.Where(path => path.KeyAt(level) == area.Key)]));
}
