namespace AddedObjectRemover;

/// <param name="MaxIndexedTriangles">A mesh with more triangles is not indexed.</param>
/// <param name="MaxClosednessTriangles">A mesh with more triangles counts as open, so the closedness check never needs memory for millions of edges.</param>
public readonly record struct MeshTreeLimits(int MaxIndexedTriangles, int MaxClosednessTriangles);
