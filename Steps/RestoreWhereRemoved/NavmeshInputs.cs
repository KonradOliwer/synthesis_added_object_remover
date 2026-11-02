using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the guards read about the navmeshes the cleaned mod changed or added.</summary>
internal static class NavmeshInputs
{
    /// <summary>
    /// The triangles of every navmesh the cleaned mod changed or added at its winning version, each with the cell holding that version,
    /// in key order of the navmeshes.
    /// </summary>
    public static ImmutableArray<NavmeshTriangleInCell> ChangedNavmeshTriangles(CollectedObjects collected) =>
    [
        .. collected.ChangedNavmeshes
            .Where(navmesh => !navmesh.Winner.IsDeleted)
            .OrderBy(navmesh => navmesh.Navmesh, RecordKeyOrder.Comparer)
            .SelectMany(navmesh => navmesh.Winner.WorldTriangles().Select(triangle => new NavmeshTriangleInCell(navmesh.Navmesh, navmesh.Winner.Cell, triangle))),
    ];

    /// <summary>Every navmesh whose triangles the cleaned mod changed or added, which all stay changed while the navmeshes are switched off.</summary>
    public static ImmutableArray<RecordKey> EveryNavmeshWithChangedTriangles(CollectedObjects collected) =>
        collected.ChangedNavmeshes.Where(navmesh => navmesh.ChangesTriangles).Select(navmesh => navmesh.Navmesh).Order(RecordKeyOrder.Comparer).ToImmutableArray();
}
