namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// Narrow phase of the ObjectShape zone: another object's mesh reaches the zone when one of its
/// triangles intersects a triangle of the enlarged mesh (the bubble), or when it lies entirely
/// inside the bubble (a closed bubble: some part of it; an open bubble: its box).
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
        MeshContact.TouchesOrEnclosesPart(bubble, zone.Bubble, other, otherTransform, IntersectionTolerance, scratch)
        || IsInsideOpenBubbleBox(bubble, zone, other, otherTransform);

    /// <summary>An open bubble has no inside: the other object counts as inside only when its whole mesh box lies in the zone's box.</summary>
    private static bool IsInsideOpenBubbleBox(MeshTriangleTree bubble, ShapeZone zone, MeshTriangleTree other, PlacedTransform otherTransform) =>
        !bubble.IsClosed && zone.Box.Contains(OrientedBox.FromLocal(other.Bounds, otherTransform));
}
