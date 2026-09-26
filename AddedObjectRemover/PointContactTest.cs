using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a point (in a mesh's local frame) is in contact with that mesh: within the tolerance of
/// one of its triangles, or embedded in it. A point counts as embedded when a line through it
/// along the mesh's local Z axis crosses the mesh an odd number of times both above and below it,
/// which is exact for closed meshes; open meshes (e.g. rocks without a bottom) are never treated
/// as enclosing a point, only their surface counts.
/// </summary>
internal static class PointContactTest
{
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool IsInContact(MeshTriangleTree tree, Vector3 point, float tolerance, List<int> scratch) =>
        IsNearSurface(tree, point, tolerance, scratch) || IsEnclosed(tree, point, scratch);

    private static bool IsNearSurface(MeshTriangleTree tree, Vector3 point, float tolerance, List<int> scratch)
    {
        var reach = new Vector3(tolerance);
        tree.CollectLeafTriangles(new Box(point - reach, point + reach), scratch);
        var toleranceSquared = tolerance * tolerance;
        foreach (var index in scratch)
        {
            var triangle = tree.GetTriangle(index);
            if (Geometry.DistanceSquaredToTriangle(point, triangle.A, triangle.B, triangle.C) <= toleranceSquared) return true;
        }
        return false;
    }

    private static bool IsEnclosed(MeshTriangleTree tree, Vector3 point, List<int> scratch)
    {
        var bounds = tree.Bounds;
        if (!bounds.Contains(point)) return false;

        tree.CollectLeafTriangles(new Box(point with { Z = bounds.Min.Z }, point with { Z = bounds.Max.Z }), scratch);
        var above = 0;
        var below = 0;
        foreach (var index in scratch)
        {
            if (!TryGetVerticalCrossing(tree.GetTriangle(index), point, out var z)) continue;
            if (z > point.Z) above++;
            else if (z < point.Z) below++;
        }
        return above % 2 == 1 && below % 2 == 1;
    }

    /// <summary>
    /// Height at which the vertical line through the point crosses the triangle's interior.
    /// Crossings exactly on an edge are not counted; sample points almost never land there.
    /// </summary>
    private static bool TryGetVerticalCrossing(MeshTriangle triangle, Vector3 point, out float z)
    {
        var edgeBc = Cross2D(triangle.B, triangle.C, point);
        var edgeCa = Cross2D(triangle.C, triangle.A, point);
        var edgeAb = Cross2D(triangle.A, triangle.B, point);
        var inside = (edgeBc > 0 && edgeCa > 0 && edgeAb > 0) || (edgeBc < 0 && edgeCa < 0 && edgeAb < 0);
        if (!inside)
        {
            z = 0;
            return false;
        }

        // Each edge function is twice the signed area of the sub-triangle opposite a corner, so it is that corner's barycentric weight.
        var twiceArea = edgeBc + edgeCa + edgeAb;
        z = (edgeBc * triangle.A.Z + edgeCa * triangle.B.Z + edgeAb * triangle.C.Z) / twiceArea;
        return true;
    }

    /// <summary>Z of (end - start) x (point - start) in the XY plane.</summary>
    private static float Cross2D(Vector3 start, Vector3 end, Vector3 point) =>
        (end.X - start.X) * (point.Y - start.Y) - (end.Y - start.Y) * (point.X - start.X);
}
