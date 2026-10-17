namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What the decisions need from one cell's terrain versions, kept instead of the decoded versions.</summary>
/// <param name="ReferenceHeights">The reference terrain's heights; null where it has none.</param>
/// <param name="WinnerHeights">The heights of the version the game shows; null where it has none.</param>
/// <param name="Changes">Per vertex, row by row as the heights; null when no counting plugin changed any vertex of the cell.</param>
internal sealed record CellTerrain(float[]? ReferenceHeights, float[]? WinnerHeights, VertexChange[]? Changes)
{
    public const int VertexCount = TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide;
}

/// <summary>A vertex where the winner differs from the reference and a counting plugin's own edit made the difference.</summary>
/// <param name="Shown">How the winner differs from the reference: the change the game shows; no change at the other vertices.</param>
/// <param name="Changer">
/// The load-order position of the counting plugin whose edit made the shown height or terrain type difference; the later
/// loading one when both did. An object names the last loading changer over all its vertices.
/// </param>
internal readonly record struct VertexChange(TerrainChange Shown, int Changer);

/// <summary>A cell under an object's check area, with the vertices of the area in it.</summary>
internal sealed record CheckedCell(CellTerrain Terrain, int[] Vertices);
