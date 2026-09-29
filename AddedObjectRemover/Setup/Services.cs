namespace AddedObjectRemover;

/// <summary>The keyed, read-only asset services the steps share; each value depends only on its key.</summary>
internal sealed record Services(ShapeCatalog Shapes, TriangleStore Triangles, NpcBodyCache Bodies, IBaseFacts Bases);
