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
    /// Called only when no triangles intersect. A closed bubble then holds each part (NIF shape) of
    /// the other mesh wholly or not at all, so one vertex per part decides; the parts are separate
    /// pieces, so any one of them inside counts. An open bubble has no inside: the other object
    /// counts as inside only when its whole mesh box lies in the zone's box.
    /// </summary>
    private static bool IsInside(
        MeshTriangleTree bubble,
        ShapeZone zone,
        MeshTriangleTree other,
        PlacedTransform otherTransform,
        TouchScratch scratch)
    {
        if (!bubble.IsClosed) return zone.Box.Contains(OrientedBox.FromLocal(other.Bounds, otherTransform));
        foreach (var firstTriangle in other.PartFirstTriangles)
        {
            var vertex = otherTransform.ToWorld(other.GetTriangle(firstTriangle).A);
            if (SurroundingRayTest.IsSurrounded(bubble, zone.Bubble.ToLocal(vertex), zone.Bubble.Rotation, scratch.NearbyTriangles)) return true;
        }
        return false;
    }
}
