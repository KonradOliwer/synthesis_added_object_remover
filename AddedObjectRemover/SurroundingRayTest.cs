using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a placed mesh surrounds a point: rays from the point straight up and towards world east,
/// west, north and south all hit the mesh. A roof alone (bridge, overhang) is hit only
/// from below, so only buildings, caves and similar shells surround a point. Rays are cast in the
/// mesh's local frame, where they end at the mesh's bounds, so every hit lies within the object.
/// </summary>
internal static class SurroundingRayTest
{
    /// <summary>
    /// Smallest sine of the angle between a ray and a triangle's plane for which the ray can hit
    /// the triangle; below it the ray runs along the plane and the intersection determinant is
    /// rounding noise.
    /// </summary>
    private const float MinHitSine = 1e-6f;

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

    /// <summary>
    /// Möller-Trumbore intersection. Hits on a triangle's edges and corners count, so a ray through
    /// a mesh's seams or symmetry planes still hits it. A NaN anywhere means no hit.
    /// </summary>
    internal static bool RayHitsTriangle(Vector3 origin, Vector3 direction, MeshTriangle triangle)
    {
        var edge1 = triangle.B - triangle.A;
        var edge2 = triangle.C - triangle.A;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        // determinant = -direction . (edge1 x edge2), so this compares the sine of the ray-to-plane angle.
        if (!(MathF.Abs(determinant) > MinHitSine * direction.Length() * Vector3.Cross(edge1, edge2).Length())) return false;

        var inverse = 1f / determinant;
        var toOrigin = origin - triangle.A;
        var u = Vector3.Dot(toOrigin, p) * inverse;
        if (u is not (>= 0f and <= 1f)) return false;
        var q = Vector3.Cross(toOrigin, edge1);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v is not >= 0f || !(u + v <= 1f)) return false;
        return Vector3.Dot(edge2, q) * inverse is >= 0f;
    }
}
