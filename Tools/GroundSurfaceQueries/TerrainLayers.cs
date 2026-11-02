using System.Numerics;

namespace AddedObjectRemover;

/// <summary>The four texture quadrants of a cell's terrain; each covers 17 by 17 vertices, and the quadrants share the vertices on their common edges.</summary>
public static class TerrainQuadrants
{
    public const int PerSide = 2;
    public const int Count = PerSide * PerSide;
    private const int SquaresPerSide = TerrainGrid.SquaresPerSide / PerSide;
    public const int VerticesPerSide = SquaresPerSide + 1;
    public const int VertexCount = VerticesPerSide * VerticesPerSide;
    private const float Size = ExteriorGrid.CellSize / PerSide;
    private const float HalfWay = 0.5f;

    /// <summary>The position of a quadrant in <see cref="TerrainLayers.Quadrants"/>: bottom row first, left before right.</summary>
    public static int IndexOf(int quadrantX, int quadrantY) => quadrantY * PerSide + quadrantX;

    /// <summary>The quadrants holding a vertex of the cell's grid (row by row from the bottom, x fastest), ascending: one, or two or four on their common edges.</summary>
    public static IEnumerable<int> Holding(int vertex) =>
        from quadrantY in Along(vertex / TerrainGrid.VerticesPerSide)
        from quadrantX in Along(vertex % TerrainGrid.VerticesPerSide)
        select IndexOf(quadrantX, quadrantY);

    /// <summary>Per quadrant, in the order of <see cref="IndexOf"/>, how many of the vertices it holds; a vertex on a common edge counts in each.</summary>
    public static int[] CountPerQuadrant(IEnumerable<int> vertices)
    {
        var counts = new int[Count];
        foreach (var quadrant in vertices.SelectMany(Holding)) counts[quadrant]++;
        return counts;
    }

    /// <summary>The world position, seen from above, of the centre of a quadrant of an exterior cell.</summary>
    public static Vector2 CentreOf(int cellX, int cellY, int quadrant) =>
        new Vector2(cellX, cellY) * ExteriorGrid.CellSize + new Vector2(quadrant % PerSide + HalfWay, quadrant / PerSide + HalfWay) * Size;

    private static IEnumerable<int> Along(int coordinate) =>
        Enumerable.Range(0, PerSide).Where(quadrant => coordinate >= quadrant * SquaresPerSide && coordinate <= (quadrant + 1) * SquaresPerSide);
}

/// <summary>The texture layers of one land record.</summary>
/// <param name="Quadrants">One entry per quadrant, in the order of <see cref="TerrainQuadrants.IndexOf"/> (bottom left, bottom right, top left, top right).</param>
public sealed record TerrainLayers(QuadrantLayers[] Quadrants);

/// <param name="Base">The texture of the quadrant's base layer; null where the quadrant has none.</param>
/// <param name="Alphas">The alpha layers laid over the base, in the order stored.</param>
public sealed record QuadrantLayers(RecordKey? Base, TerrainAlphaLayer[] Alphas);

/// <param name="Texture">The landscape texture this layer paints.</param>
/// <param name="LayerNumber">Layers are laid over each other from the lowest number up.</param>
/// <param name="Opacities">Per vertex of the quadrant (<see cref="TerrainQuadrants.VerticesPerSide"/> per row, 0 to 1); 0 where the layer paints nothing.</param>
public sealed record TerrainAlphaLayer(RecordKey Texture, int LayerNumber, float[] Opacities);

/// <param name="Texture">The landscape texture; null for the game's default terrain texture (a quadrant without a base layer).</param>
/// <param name="Share">Its part of the vertex's appearance, above 0 and at most 1.</param>
public readonly record struct TextureShare(RecordKey? Texture, float Share);
