using System.Numerics;

namespace AddedObjectRemover;

/// <summary>The heights of the grid points just outside one cell's grid, in the neighbouring cells; each side is null where its heights are not known.</summary>
/// <param name="West">At x = -1, per y from the bottom.</param>
/// <param name="East">At x = <see cref="TerrainGrid.VerticesPerSide"/>, per y from the bottom.</param>
/// <param name="South">At y = -1, per x.</param>
/// <param name="North">At y = <see cref="TerrainGrid.VerticesPerSide"/>, per x.</param>
public sealed record CellEdgeHeights(float[]? West, float[]? East, float[]? South, float[]? North)
{
    /// <summary>The second line of a neighbour's grid, which lies one grid step beyond the shared edge.</summary>
    private const int SecondLine = 1;

    private const int SecondLastLine = TerrainGrid.VerticesPerSide - 2;

    public static CellEdgeHeights Unknown { get; } = new(null, null, null, null);

    /// <summary>The heights next to a cell's edges, taken from the grids of its four neighbouring cells.</summary>
    /// <param name="west">The heights of the cell to the west, row by row from the bottom, x fastest; null where not known. Likewise the others.</param>
    public static CellEdgeHeights Beside(float[]? west, float[]? east, float[]? south, float[]? north) =>
        new(Column(west, SecondLastLine), Column(east, SecondLine), Row(south, SecondLastLine), Row(north, SecondLine));

    private static float[]? Column(float[]? heights, int x) =>
        heights is null ? null : [.. Enumerable.Range(0, TerrainGrid.VerticesPerSide).Select(y => heights[y * TerrainGrid.VerticesPerSide + x])];

    private static float[]? Row(float[]? heights, int y) =>
        heights is null ? null : [.. Enumerable.Range(0, TerrainGrid.VerticesPerSide).Select(x => heights[y * TerrainGrid.VerticesPerSide + x])];
}

/// <summary>The normals of a cell's terrain surface at its grid points.</summary>
public static class TerrainNormals
{
    private const int Last = TerrainGrid.VerticesPerSide - 1;

    /// <param name="heights">In game units, row by row from the bottom, x fastest.</param>
    /// <returns>
    /// Per grid point, the upward unit normal from the slopes along x and along y: from both neighbours where both are known,
    /// from the one known neighbour at an edge whose other side is not, flat along an axis with neither.
    /// </returns>
    public static Vector3[] Compute(IReadOnlyList<float> heights, CellEdgeHeights beyond)
    {
        var normals = new Vector3[heights.Count];
        const int side = TerrainGrid.VerticesPerSide;
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var point = y * side + x;
                var slopeX = Slope(x > 0 ? heights[point - 1] : beyond.West?[y], heights[point], x < Last ? heights[point + 1] : beyond.East?[y]);
                var slopeY = Slope(y > 0 ? heights[point - side] : beyond.South?[x], heights[point], y < Last ? heights[point + side] : beyond.North?[x]);
                normals[point] = Vector3.Normalize(new Vector3(-slopeX, -slopeY, 1f));
            }
        }
        return normals;
    }

    private static float Slope(float? before, float at, float? after) => (before, after) switch
    {
        ({ } low, { } high) => (high - low) / (2 * TerrainGrid.VertexSpacing),
        (null, { } high) => (high - at) / TerrainGrid.VertexSpacing,
        ({ } low, null) => (at - low) / TerrainGrid.VertexSpacing,
        _ => 0f,
    };
}
