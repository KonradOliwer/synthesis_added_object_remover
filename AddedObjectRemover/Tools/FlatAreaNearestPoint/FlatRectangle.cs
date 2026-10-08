using System.Numerics;

namespace AddedObjectRemover;

/// <summary>An axis-aligned rectangle on the ground plane.</summary>
public readonly record struct FlatRectangle(Vector2 Min, Vector2 Max);
