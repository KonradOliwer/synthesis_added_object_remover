using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Where a ray meets a mesh, in the mesh's local frame. Hits on a triangle's edges and corners count.</summary>
public static class RayHit
{
    /// <summary>
    /// Smallest sine of the angle between a ray and a triangle's plane for which the ray can hit
    /// the triangle; below it the ray runs along the plane and the intersection determinant is
    /// rounding noise.
    /// </summary>
    private const float MinHitSine = 1e-6f;

    /// <summary>The nearest point of the mesh along the ray, as the distance in units of <paramref name="direction"/>.</summary>
    /// <param name="counts">Whether a hit on this triangle at this distance counts; the others are passed through.</param>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    /// <returns>False when the ray hits no triangle that counts.</returns>
    public static bool TryFindNearest(
        MeshTriangleTree tree, Vector3 origin, Vector3 direction, Func<MeshTriangle, float, bool> counts, List<int> scratch, out float distance)
    {
        tree.CollectLeafTrianglesAlongRay(origin, direction, scratch);
        distance = float.PositiveInfinity;
        foreach (var index in scratch)
        {
            var triangle = tree.GetTriangle(index);
            if (TryHitTriangle(origin, direction, triangle, out var hit) && hit < distance && counts(triangle, hit)) distance = hit;
        }
        return distance < float.PositiveInfinity;
    }

    /// <summary>Möller-Trumbore intersection. A NaN anywhere means no hit.</summary>
    /// <param name="distance">The hit's distance along the ray, in units of <paramref name="direction"/>; at least 0.</param>
    internal static bool TryHitTriangle(Vector3 origin, Vector3 direction, MeshTriangle triangle, out float distance)
    {
        distance = 0f;
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
        distance = Vector3.Dot(edge2, q) * inverse;
        return distance is >= 0f;
    }
}
