using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a triangle overlaps a solid axis-aligned box (separating axis test, Akenine-Möller):
/// true when the triangle crosses the box's surface or lies inside it. Touching counts, so a box
/// flattened to a plane, line or point can still be hit. A NaN anywhere means no overlap.
/// </summary>
internal static class TriangleBoxOverlap
{
    public static bool Overlaps(MeshTriangle triangle, Box box)
    {
        var center = box.Center;
        var halfSize = box.Size * 0.5f;
        var a = triangle.A - center;
        var b = triangle.B - center;
        var c = triangle.C - center;

        if (Separates(Vector3.UnitX, a, b, c, halfSize)
            || Separates(Vector3.UnitY, a, b, c, halfSize)
            || Separates(Vector3.UnitZ, a, b, c, halfSize)) return false;

        var edgeAb = b - a;
        var edgeBc = c - b;
        var edgeCa = a - c;
        if (SeparatesOnEdgeAxes(edgeAb, a, b, c, halfSize)
            || SeparatesOnEdgeAxes(edgeBc, a, b, c, halfSize)
            || SeparatesOnEdgeAxes(edgeCa, a, b, c, halfSize)) return false;

        return !Separates(Vector3.Cross(edgeAb, edgeBc), a, b, c, halfSize);
    }

    /// <summary>The three axes perpendicular to both the edge and one box axis.</summary>
    private static bool SeparatesOnEdgeAxes(Vector3 edge, Vector3 a, Vector3 b, Vector3 c, Vector3 halfSize) =>
        Separates(Vector3.Cross(Vector3.UnitX, edge), a, b, c, halfSize)
        || Separates(Vector3.Cross(Vector3.UnitY, edge), a, b, c, halfSize)
        || Separates(Vector3.Cross(Vector3.UnitZ, edge), a, b, c, halfSize);

    /// <remarks>
    /// Written as "not overlapping" so that a NaN separates. A zero axis (an edge parallel to a
    /// box axis, or a degenerate triangle's normal) projects everything to 0 and never separates.
    /// </remarks>
    private static bool Separates(Vector3 axis, Vector3 a, Vector3 b, Vector3 c, Vector3 halfSize)
    {
        var pa = Vector3.Dot(a, axis);
        var pb = Vector3.Dot(b, axis);
        var pc = Vector3.Dot(c, axis);
        var boxReach = Vector3.Dot(halfSize, Vector3.Abs(axis));
        var min = MathF.Min(pa, MathF.Min(pb, pc));
        var max = MathF.Max(pa, MathF.Max(pb, pc));
        return !(min <= boxReach && max >= -boxReach);
    }
}
