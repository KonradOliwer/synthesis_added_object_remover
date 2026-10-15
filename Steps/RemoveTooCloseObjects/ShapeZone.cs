namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// The ObjectShape removal zone of one placed object: its mesh placed as the bubble, which other meshes must reach,
/// within a box. Each <see cref="ZoneGrowth"/> builds its own.
/// </summary>
/// <param name="Box">The zone's box; the same zone as the BoundingBox mode.</param>
/// <param name="Bubble">Places the object's mesh-local triangles as the zone's mesh in the world.</param>
/// <param name="ContactTolerance">How far (world units) another mesh may be from the bubble and still reach the zone.</param>
internal readonly record struct ShapeZone(OrientedBox Box, PlacedTransform Bubble, float ContactTolerance);
