namespace AddedObjectRemover;

/// <summary>
/// Whether an NPC's body is stuck in an object at the object's real size: a body triangle
/// intersects an object triangle, or else the body lies inside the object, decided by one body
/// vertex being surrounded by the object's mesh (<see cref="SurroundingRayTest"/>).
/// </summary>
internal static class NpcStuckTest
{
    /// <summary>Triangles must really intersect; merely coming close is not enough.</summary>
    private const float IntersectionTolerance = 0f;

    public static bool IsStuck(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        MeshTriangleTree bodyTree,
        PlacedTransform bodyTransform,
        TouchScratch scratch) =>
        MeshTouchTest.Touches(objectTree, objectTransform, bodyTree, bodyTransform, IntersectionTolerance, scratch)
        || IsInside(objectTree, objectTransform, bodyTree, bodyTransform, scratch);

    /// <summary>Called only when no triangles intersect, so the body is wholly inside or wholly outside and one vertex decides.</summary>
    private static bool IsInside(
        MeshTriangleTree objectTree,
        PlacedTransform objectTransform,
        MeshTriangleTree bodyTree,
        PlacedTransform bodyTransform,
        TouchScratch scratch)
    {
        var vertex = bodyTransform.ToWorld(bodyTree.GetTriangle(0).A);
        return SurroundingRayTest.IsSurrounded(objectTree, objectTransform.ToLocal(vertex), objectTransform.Rotation, scratch.NearbyTriangles);
    }
}
