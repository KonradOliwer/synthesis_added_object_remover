using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the terrain plan reads about the land of the collected objects.</summary>
internal static class LandInputs
{
    /// <summary>
    /// Each cell's heights before the cleaned mod and at the winning version: for a LAND record the cleaned mod changed, those it
    /// carries; for any other, and one that carries none, its versions read, each cell once.
    /// </summary>
    public static Func<ExteriorCell, LandHeights> LandHeightsOf(CollectedObjects collected, Func<ExteriorCell, LandHeights> readLandHeights)
    {
        var changed = new Dictionary<ExteriorCell, LandHeights>();
        foreach (var land in collected.ChangedLands)
        {
            if (land.Heights is { } heights) changed.TryAdd(land.Cell, heights);
        }
        var read = new ComputedOncePerKey<ExteriorCell, LandHeights>(Publication.BuiltOnce, EqualityComparer<ExteriorCell>.Default);
        return cell => changed.TryGetValue(cell, out var heights) ? heights : read.Get(cell, () => readLandHeights(cell));
    }

    /// <summary>Per worldspace holding land, the spaces holding an object of another plugin whose ground it is, in key order.</summary>
    public static Dictionary<RecordKey, ImmutableArray<RecordKey>> ObjectSpacesOnLand(CollectedObjects collected, Func<RecordKey, RecordKey?> findLandWorldspace) =>
        ByLand(collected.OtherModAndSupportOnlyObjects.Select(other => other.SpaceKey).Distinct(), findLandWorldspace);

    /// <summary>Per worldspace holding land, the spaces holding a target whose ground it is, in key order.</summary>
    public static Dictionary<RecordKey, ImmutableArray<RecordKey>> SpacesOnLand(CollectedObjects collected, Func<RecordKey, RecordKey?> findLandWorldspace) =>
        ByLand(collected.TargetSpaces(), findLandWorldspace);

    /// <param name="spaces">Each once.</param>
    private static Dictionary<RecordKey, ImmutableArray<RecordKey>> ByLand(IEnumerable<RecordKey> spaces, Func<RecordKey, RecordKey?> findLandWorldspace) =>
        spaces
            .Select(space => (Space: space, Land: findLandWorldspace(space)))
            .Where(pair => pair.Land is not null)
            .GroupBy(pair => pair.Land!.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Space).Order(RecordKeyOrder.Comparer).ToImmutableArray());
}
