using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a placed mesh surrounds a point: rays from the point straight up and towards world east,
/// west, north and south all hit the mesh. A roof alone (bridge, overhang) is hit only
/// from below, so only buildings, caves and similar shells surround a point. Rays are cast in the
/// mesh's local frame, where they end at the mesh's bounds, so every hit lies within the object.
/// </summary>
public static class SurroundingRayTest
{
    private static readonly Vector3[] WorldDirections = [Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY];

    /// <param name="localPoint">The point in the mesh's local frame.</param>
    /// <param name="rotation">The placed object's rotation, turning local directions into world ones.</param>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool IsSurrounded(MeshTriangleTree tree, Vector3 localPoint, Mat3 rotation, List<int> scratch)
    {
        if (!tree.Bounds.Contains(localPoint)) return false;
        foreach (var worldDirection in WorldDirections)
        {
            if (!HitsMesh(tree, localPoint, rotation.TransformTransposed(worldDirection), scratch)) return false;
        }
        return true;
    }

    private static bool HitsMesh(MeshTriangleTree tree, Vector3 origin, Vector3 direction, List<int> scratch)
    {
        tree.CollectLeafTrianglesAlongRay(origin, direction, scratch);
        foreach (var index in scratch)
        {
            if (RayHitsTriangle(origin, direction, tree.GetTriangle(index))) return true;
        }
        return false;
    }

    /// <summary>Hits on a triangle's edges and corners count, so a ray through a mesh's seams or symmetry planes still hits it.</summary>
    internal static bool RayHitsTriangle(Vector3 origin, Vector3 direction, MeshTriangle triangle) =>
        RayHit.TryHitTriangle(origin, direction, triangle, out _);
}
