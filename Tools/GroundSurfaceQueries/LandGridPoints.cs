using System.Collections;
using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>A grid point of one land record: the record's cell and the point's index in the cell's grid (row by row from the bottom, x fastest).</summary>
public readonly record struct LandGridPoint(ExteriorCell Cell, int Point);

/// <summary>A grid point of a worldspace's land, counted in grid steps from the world origin.</summary>
public readonly record struct WorldGridPoint(RecordKey Worldspace, int X, int Y)
{
    private const int Side = TerrainHeights.VerticesPerSide;
    private const int Squares = TerrainGrid.SquaresPerSide;

    public static WorldGridPoint Of(LandGridPoint point) =>
        new(point.Cell.WorldspaceKey, point.Cell.X * Squares + point.Point % Side, point.Cell.Y * Squares + point.Point / Side);

    public WorldGridPoint StepBy(GridStep step) => this with { X = X + step.X, Y = Y + step.Y };

    /// <summary>
    /// Every cell grid point at this point: one inside a cell, two on a cell edge, four on a cell corner. The cell whose grid starts
    /// at or before it on both axes comes first, then the one to the south, the one to the west and the one to the south west.
    /// </summary>
    public LandGridPointHolders Holders()
    {
        var cellX = (int)MathF.Floor((float)X / Squares);
        var cellY = (int)MathF.Floor((float)Y / Squares);
        return new LandGridPointHolders(Worldspace, cellX, X - cellX * Squares, cellY, Y - cellY * Squares);
    }
}

/// <summary>
/// The one, two or four cell grid points at a world grid point (<see cref="WorldGridPoint.Holders"/>), listed without allocating: a
/// point on a cell's first line is also the last line of the cell before it.
/// </summary>
public readonly struct LandGridPointHolders : IReadOnlyList<LandGridPoint>
{
    private const int Side = TerrainHeights.VerticesPerSide;
    private const int Squares = TerrainGrid.SquaresPerSide;

    private readonly RecordKey _worldspace;
    private readonly int _cellX;
    private readonly int _x;
    private readonly int _cellY;
    private readonly int _y;

    /// <param name="x">The point's column in the cell at <paramref name="cellX"/>.</param>
    /// <param name="y">The point's row in the cell at <paramref name="cellY"/>.</param>
    internal LandGridPointHolders(RecordKey worldspace, int cellX, int x, int cellY, int y) =>
        (_worldspace, _cellX, _x, _cellY, _y) = (worldspace, cellX, x, cellY, y);

    public int Count => CellsAlong(_x) * CellsAlong(_y);

    public LandGridPoint this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            var west = index / CellsAlong(_y) == 1;
            var south = index % CellsAlong(_y) == 1;
            var (cellX, x) = west ? (_cellX - 1, Squares) : (_cellX, _x);
            var (cellY, y) = south ? (_cellY - 1, Squares) : (_cellY, _y);
            return new LandGridPoint(new ExteriorCell(_worldspace, cellX, cellY), y * Side + x);
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<LandGridPoint> IEnumerable<LandGridPoint>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>A point on a cell's first line along an axis lies in two cells along it.</summary>
    private static int CellsAlong(int local) => local == 0 ? 2 : 1;

    public struct Enumerator(LandGridPointHolders holders) : IEnumerator<LandGridPoint>
    {
        private int _index = -1;

        public readonly LandGridPoint Current => holders[_index];

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < holders.Count;

        public void Reset() => _index = -1;

        public readonly void Dispose()
        {
        }
    }
}

/// <summary>A step from one land grid point to another, in grid steps, with its length.</summary>
public readonly record struct GridStep(int X, int Y, float Distance)
{
    /// <summary>The steps to every other grid point at most the distance away, nearest first, then by y and by x.</summary>
    public static ImmutableArray<GridStep> Within(int distance) =>
    [
        .. Enumerable.Range(-distance, 2 * distance + 1)
            .SelectMany(y => Enumerable.Range(-distance, 2 * distance + 1).Select(x => new GridStep(x, y, MathF.Sqrt(x * x + y * y))))
            .Where(step => step.Distance > 0 && step.Distance <= distance)
            .OrderBy(step => step.Distance)
            .ThenBy(step => step.Y)
            .ThenBy(step => step.X),
    ];
}
