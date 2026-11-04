using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Which diagonal splits a terrain square into its two triangles.</summary>
public enum SquareSplit { SouthWestToNorthEast, SouthEastToNorthWest }

/// <summary>The part of a rectangle in one terrain square.</summary>
/// <param name="FirstCorner">The index, in its cell, of the square's south-west corner.</param>
/// <param name="Min">The part's south-west corner, 0 to 1 on each axis from the square's south-west corner.</param>
/// <param name="Max">The part's north-east corner, likewise.</param>
public readonly record struct SquarePart(int CellX, int CellY, int FirstCorner, Vector2 Min, Vector2 Max);

/// <summary>
/// The terrain's square lattice: 33 by 33 vertices per cell, 128 units apart, 32 squares across each 4096-unit cell, and the
/// stored height of a vertex counted in steps of 8 units. The one place these values are written down.
/// </summary>
public static class TerrainGrid
{
    public const int VerticesPerSide = 33;
    public const int SquaresPerSide = VerticesPerSide - 1;
    public const int VertexCount = VerticesPerSide * VerticesPerSide;
    public const float VertexSpacing = ExteriorGrid.CellSize / SquaresPerSide;
    public const float UnitsPerHeightStep = 8f;

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

    /// <summary>The corner vertices of every terrain square the box overlaps seen from above, listed as for a rectangle.</summary>
    public static IEnumerable<CellVertices> VerticesUnder(Box area) =>
        VerticesUnder((new Vector2(area.Min.X, area.Min.Y), new Vector2(area.Max.X, area.Max.Y)));

    /// <summary>
    /// The part of the rectangle in each terrain square it overlaps, cell by cell in the order of <see cref="VerticesUnder((Vector2, Vector2))"/>,
    /// then square by square, x first.
    /// </summary>
    public static IEnumerable<SquarePart> SquaresUnder((Vector2 Min, Vector2 Max) rectangle)
    {
        var squaresX = SquareRange(rectangle.Min.X, rectangle.Max.X);
        var squaresY = SquareRange(rectangle.Min.Y, rectangle.Max.Y);
        for (var cellX = CellOf(squaresX.First); cellX <= CellOf(squaresX.Last); cellX++)
        {
            for (var cellY = CellOf(squaresY.First); cellY <= CellOf(squaresY.Last); cellY++)
            {
                var (firstX, lastX) = LocalSquares(cellX, squaresX);
                var (firstY, lastY) = LocalSquares(cellY, squaresY);
                for (var x = firstX; x <= lastX; x++)
                {
                    for (var y = firstY; y <= lastY; y++)
                    {
                        var origin = new Vector2(cellX * SquaresPerSide + x, cellY * SquaresPerSide + y) * VertexSpacing;
                        var min = Vector2.Clamp((rectangle.Min - origin) / VertexSpacing, Vector2.Zero, Vector2.One);
                        var max = Vector2.Clamp((rectangle.Max - origin) / VertexSpacing, Vector2.Zero, Vector2.One);
                        yield return new SquarePart(cellX, cellY, y * VerticesPerSide + x, min, max);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The points of a part of a square where a surface made of the square's two triangles, split as given, takes its lowest and
    /// highest values over the part: the part's corners and the points where its edges cross the split's diagonal.
    /// </summary>
    public static IEnumerable<Vector2> BoundingPointsOnTriangles(SquarePart part, SquareSplit split)
    {
        var (min, max) = (part.Min, part.Max);
        Vector2[] corners = [min, new(max.X, min.Y), new(min.X, max.Y), max];
        // Each diagonal is its own inverse: the other coordinate of a point on it is the same function of either one.
        Func<float, float> diagonal = split == SquareSplit.SouthWestToNorthEast ? static along => along : static along => 1f - along;
        var acrossX = new[] { min.X, max.X }.Select(x => new Vector2(x, diagonal(x))).Where(point => point.Y >= min.Y && point.Y <= max.Y);
        var acrossY = new[] { min.Y, max.Y }.Select(y => new Vector2(diagonal(y), y)).Where(point => point.X >= min.X && point.X <= max.X);
        return corners.Concat(acrossX).Concat(acrossY);
    }

    /// <summary>The height at a point of a square on the triangle that holds it, the square split into two triangles as given.</summary>
    /// <param name="heights">The heights of a cell, row by row.</param>
    /// <param name="firstCorner">The index of the square's south-west corner.</param>
    /// <param name="inSquare">The point, 0 to 1 on each axis from the square's south-west corner.</param>
    public static float HeightOnTriangles(float[] heights, int firstCorner, Vector2 inSquare, SquareSplit split)
    {
        var (southWest, southEast, northWest, northEast) = CornerHeights(heights, firstCorner);
        var (u, v) = (inSquare.X, inSquare.Y);
        return split switch
        {
            SquareSplit.SouthWestToNorthEast when u >= v => southWest + u * (southEast - southWest) + v * (northEast - southEast),
            SquareSplit.SouthWestToNorthEast => southWest + v * (northWest - southWest) + u * (northEast - northWest),
            _ when u + v <= 1f => southWest + u * (southEast - southWest) + v * (northWest - southWest),
            _ => northEast + (1f - u) * (northWest - northEast) + (1f - v) * (southEast - northEast),
        };
    }

    /// <summary>The indices of a square's four corners: south-west, south-east, north-west, north-east.</summary>
    public static int[] CornersOf(int firstCorner) => [firstCorner, firstCorner + 1, firstCorner + VerticesPerSide, firstCorner + VerticesPerSide + 1];

    /// <summary>The heights of a square's four corners.</summary>
    /// <param name="heights">The heights of a cell, row by row.</param>
    /// <param name="firstCorner">The index of the square's south-west corner.</param>
    public static (float SouthWest, float SouthEast, float NorthWest, float NorthEast) CornerHeights(float[] heights, int firstCorner) =>
        (heights[firstCorner], heights[firstCorner + 1], heights[firstCorner + VerticesPerSide], heights[firstCorner + VerticesPerSide + 1]);

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
            for (var x = localFirstX; x <= localLastX + 1; x++) indices.Add(y * VerticesPerSide + x);
        }
        return [.. indices];
    }

    private static (int First, int Last) LocalSquares(int cell, (int First, int Last) squares)
    {
        var cellFirst = cell * SquaresPerSide;
        return (Math.Max(squares.First, cellFirst) - cellFirst, Math.Min(squares.Last, cellFirst + SquaresPerSide - 1) - cellFirst);
    }
}
