namespace AddedObjectRemover;

/// <summary>The four texture quadrants of a cell's terrain; each covers 17 by 17 vertices, and the quadrants share the vertices on their common edges.</summary>
public static class TerrainQuadrants
{
    public const int PerSide = 2;
    public const int VerticesPerSide = 17;
    public const int VertexCount = VerticesPerSide * VerticesPerSide;

    /// <summary>The position of a quadrant in <see cref="TerrainLayers.Quadrants"/>: bottom row first, left before right.</summary>
    public static int IndexOf(int quadrantX, int quadrantY) => quadrantY * PerSide + quadrantX;
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
