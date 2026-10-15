using System.Numerics;

namespace AddedObjectRemover;

/// <summary>The terrain's square lattice: vertices 128 units apart, 32 squares across each 4096-unit cell.</summary>
public static class TerrainGrid
{
    public const int SquaresPerSide = TerrainHeights.VerticesPerSide - 1;
    public const float VertexSpacing = ExteriorGrid.CellSize / SquaresPerSide;

    /// <summary>
    /// The corner vertices of every terrain square the rectangle overlaps (a rectangle that only touches a square's edge does not
    /// overlap it), cell by cell, in ascending cell order (x first). A vertex on a cell's edge is listed in each cell whose square
    /// has it as a corner, in that cell's own indices.
    /// </summary>
    public static IEnumerable<CellVertices> VerticesUnder((Vector2 Min, Vector2 Max) rectangle)
    {
        var (firstX, lastX) = SquareRange(rectangle.Min.X, rectangle.Max.X);
        var (firstY, lastY) = SquareRange(rectangle.Min.Y, rectangle.Max.Y);
        for (var cellX = CellOf(firstX); cellX <= CellOf(lastX); cellX++)
        {
            for (var cellY = CellOf(firstY); cellY <= CellOf(lastY); cellY++)
            {
                yield return new CellVertices(cellX, cellY, CornerIndices(cellX, cellY, (firstX, lastX), (firstY, lastY)));
            }
        }
    }

    /// <returns>The first and last square index (counted from the world origin) the interval overlaps; a zero-width interval overlaps the square it starts in.</returns>
    private static (int First, int Last) SquareRange(float min, float max)
    {
        var first = (int)MathF.Floor(min / VertexSpacing);
        var last = (int)MathF.Ceiling(max / VertexSpacing) - 1;
        return (first, Math.Max(first, last));
    }

    private static int CellOf(int square) => (int)MathF.Floor((float)square / SquaresPerSide);

    private static int[] CornerIndices(int cellX, int cellY, (int First, int Last) squaresX, (int First, int Last) squaresY)
    {
        var (localFirstX, localLastX) = LocalSquares(cellX, squaresX);
        var (localFirstY, localLastY) = LocalSquares(cellY, squaresY);
        var indices = new List<int>();
        for (var y = localFirstY; y <= localLastY + 1; y++)
        {
            for (var x = localFirstX; x <= localLastX + 1; x++) indices.Add(y * TerrainHeights.VerticesPerSide + x);
        }
        return [.. indices];
    }

    private static (int First, int Last) LocalSquares(int cell, (int First, int Last) squares)
    {
        var cellFirst = cell * SquaresPerSide;
        return (Math.Max(squares.First, cellFirst) - cellFirst, Math.Min(squares.Last, cellFirst + SquaresPerSide - 1) - cellFirst);
    }
}
