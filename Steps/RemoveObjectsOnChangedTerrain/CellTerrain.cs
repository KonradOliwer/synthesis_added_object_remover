namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What the decisions need from one cell's terrain versions, kept instead of the decoded versions.</summary>
/// <param name="ReferenceHeights">The reference terrain's heights; null where it has none.</param>
/// <param name="WinnerHeights">The heights of the version the game shows; null where it has none.</param>
/// <param name="Changes">
/// Per vertex, row by row as the heights; a vertex no counting plugin changed has no changer. Null when no counting plugin
/// changed any vertex of the cell.
/// </param>
internal sealed record CellTerrain(float[]? ReferenceHeights, float[]? WinnerHeights, VertexChange[]? Changes)
{
    public const int VertexCount = TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide;
}

/// <summary>The counting plugins whose own edits made the winner differ from the reference at a vertex, per check.</summary>
/// <param name="HeightChanger">The load-order position of the counting plugin that made the shown height difference; null where none did.</param>
/// <param name="TypeChanger">The load-order position of the counting plugin that made the shown terrain type difference; null where none did.</param>
internal readonly record struct VertexChange(int? HeightChanger, int? TypeChanger)
{
    public bool Any => HeightChanger != null || TypeChanger != null;
}

/// <summary>A cell under an object's check area, with the vertices of the area in it.</summary>
internal sealed record CheckedCell(CellTerrain Terrain, int[] Vertices);
