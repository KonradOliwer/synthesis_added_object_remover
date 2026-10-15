namespace AddedObjectRemover;

/// <summary>The terrain vertices of one cell, as indices into the cell's height grid (row by row, <see cref="TerrainHeights.VerticesPerSide"/> per row), ascending.</summary>
public sealed record CellVertices(int CellX, int CellY, int[] VertexIndices);
