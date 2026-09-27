namespace AddedObjectRemover;

/// <summary>
/// Narrow phase of the ObjectShape zone: another object's mesh reaches the zone when one of its
/// triangles intersects a triangle of the enlarged mesh (the bubble), or when it lies entirely
/// inside the bubble.
/// </summary>
internal static class ShapeZoneContact
{
    /// <summary>Triangles must really intersect; merely coming close is not enough.</summary>
    private const float IntersectionTolerance = 0f;

    /// <param name="bubble">The zone object's own mesh, placed by <see cref="ShapeZone.Bubble"/>.</param>
    public static bool Reaches(
        MeshTriangleTree bubble,
        ShapeZone zone,
        MeshTriangleTree other,
        PlacedTransform otherTransform,
        TouchScratch scratch) =>
        MeshTouchTest.Touches(bubble, zone.Bubble, other, otherTransform, IntersectionTolerance, scratch)
        || IsInside(bubble, zone, other, otherTransform, scratch);

    /// <summary>
    /// Called only when no triangles intersect, so the other mesh is either wholly inside or wholly
    /// outside the bubble and one of its vertices decides. An open bubble has no inside, so its
    /// box stands in for it.
    /// </summary>
    private static bool IsInside(
        MeshTriangleTree bubble,
        ShapeZone zone,
        MeshTriangleTree other,
        PlacedTransform otherTransform,
        TouchScratch scratch)
    {
        var vertex = otherTransform.ToWorld(other.GetTriangle(0).A);
        if (!bubble.IsClosed) return zone.Box.Contains(vertex);
        return SurroundingRayTest.IsSurrounded(bubble, zone.Bubble.ToLocal(vertex), zone.Bubble.Rotation, scratch.NearbyTriangles);
    }
}
