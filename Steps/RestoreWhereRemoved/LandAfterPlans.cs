using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The ground and water the restore's plans leave, as the removal steps' follow-up reads them: the ground as it is with the heights the
/// height map stores in the cells with a point going back, where it goes down, the ground without the cleaned mod's height changes, and
/// the water lines the cleaned mod moved whose level stays. Thread-safe.
/// </summary>
/// <param name="now">The ground as it is.</param>
/// <param name="spacesOnLand">Per worldspace holding land, the spaces whose ground it is, in key order.</param>
/// <param name="heightTolerance">Heights closer than this count as equal.</param>
/// <param name="withoutCleanedMod">The ground as it is with every height the cleaned mod changed at its height before the cleaned mod.</param>
internal sealed class LandAfterPlans(
    ITerrainHeights now,
    Func<RecordKey, RecordKey?> findLandWorldspace,
    IReadOnlyDictionary<RecordKey, ImmutableArray<RecordKey>> spacesOnLand,
    float heightTolerance,
    ITerrainHeights withoutCleanedMod)
{
    /// <summary>
    /// The ground as it is with every point the cleaned mod's LAND records change back at the height of the version below the cleaned
    /// mod's; a record holding no height change, or a version without a height map, leaves its cell as it is.
    /// </summary>
    public static ITerrainHeights WithoutCleanedMod(IEnumerable<LandChangedByCleanedMod> lands, ITerrainHeights now, Func<RecordKey, RecordKey?> findLandWorldspace)
    {
        var replaced = new Dictionary<ExteriorCell, float[]>();
        foreach (var land in lands.Where(land => land.Heights is { Earlier: not null, Current: not null } && !land.UndoPoints.IsEmpty))
        {
            float[] heights = [.. land.Heights!.Current!];
            foreach (var point in land.UndoPoints.Points) heights[point] = land.Heights.Earlier![point];
            replaced.TryAdd(land.Cell, heights);
        }
        return new GroundWithCellsReplaced(now, replaced, findLandWorldspace);
    }

    public LandAfter Of(TerrainUndoPlan terrain, WaterUndoPlan water)
    {
        var stored = terrain.CellsWithRestoredPoints()
            .OrderBy(cell => cell.WorldspaceKey, RecordKeyOrder.Comparer)
            .ThenBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .Select(cell => (
                Cell: cell,
                Stored: LandHeightEncoder.Stored(terrain.PlannedHeightsOf(cell)!) ?? throw new UnreachableException("The restore planned heights the height map cannot store."),
                Now: terrain.CurrentHeightsOf(cell) ?? throw new UnreachableException($"Cell {cell} has a point going back but no heights now.")))
            .ToList();
        return new LandAfter(
            new GroundWithCellsReplaced(now, stored.ToDictionary(entry => entry.Cell, entry => entry.Stored), findLandWorldspace),
            [.. stored.SelectMany(entry => LoweredIn(entry.Cell, entry.Stored, entry.Now))],
            withoutCleanedMod,
            water.LinesThatStay());
    }

    /// <summary>Per space on the cell's land, the box over the cell's points stored lower than they are now; none when no point goes down.</summary>
    private IEnumerable<LoweredGround> LoweredIn(ExteriorCell cell, float[] stored, float[] now)
    {
        List<int> down = [.. Enumerable.Range(0, stored.Length).Where(point => stored[point] < now[point] - heightTolerance)];
        if (down.Count == 0) return [];
        var corner = new Vector2(cell.X, cell.Y) * ExteriorGrid.CellSize;
        List<Vector3> ends =
        [
            .. down.SelectMany(point =>
            {
                var position = corner + new Vector2(point % TerrainGrid.VerticesPerSide, point / TerrainGrid.VerticesPerSide) * TerrainGrid.VertexSpacing;
                return new[] { new Vector3(position, stored[point]), new Vector3(position, now[point]) };
            }),
        ];
        var box = new Box(ends.Aggregate(Vector3.Min), ends.Aggregate(Vector3.Max));
        var upright = new OrientedBox(box.Center, Mat3.Identity, box.HalfExtents);
        return spacesOnLand.GetValueOrDefault(cell.WorldspaceKey, []).Select(space => new LoweredGround(space, upright));
    }
}
