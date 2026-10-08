using System.Numerics;

namespace AddedObjectRemover;

public static class TriangleDistance
{
    /// <summary>Squared sine of the smallest angle at a triangle's first corner for which its face is used; see <see cref="ClosestPointOnTriangle"/>.</summary>
    private const float MinFaceSineSquared = 1e-6f;

    public static float DistanceSquaredToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) =>
        (p - ClosestPointOnTriangle(p, a, b, c)).LengthSquared();

    /// <summary>
    /// The point of triangle (a, b, c) closest to <paramref name="p"/> (Ericson, Real-Time
    /// Collision Detection 5.1.5). A triangle whose angle at a has a squared sine at or below
    /// <see cref="MinFaceSineSquared"/> (coincident vertices, collinear or nearly collinear
    /// vertices, a single point) is treated as its three edges: its region weights would be
    /// rounding noise, and its face lies within 1e-3 of an edge length of those edges.
    /// </summary>
    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        if (!(Vector3.Cross(ab, ac).LengthSquared() > MinFaceSineSquared * ab.LengthSquared() * ac.LengthSquared()))
        {
            return ClosestPointOnEdges(p, a, b, c);
        }

        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var abDenominator = d1 - d3;
            var v = abDenominator != 0 ? d1 / abDenominator : 0f;
            return a + v * ab;
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var acDenominator = d2 - d6;
            var w = acDenominator != 0 ? d2 / acDenominator : 0f;
            return a + w * ac;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            var bcDenominator = (d4 - d3) + (d5 - d6);
            var w = bcDenominator != 0 ? (d4 - d3) / bcDenominator : 0f;
            return b + w * (c - b);
        }

        // Equals |ab x ac|^2 in exact arithmetic; rounding can still cancel it when p is far from the triangle.
        var sum = va + vb + vc;
        if (!(sum > 0)) return ClosestPointOnEdges(p, a, b, c);
        var denominator = 1f / sum;
        var vv = vb * denominator;
        var ww = vc * denominator;
        return a + ab * vv + ac * ww;
    }

    private static Vector3 ClosestPointOnEdges(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var closest = ClosestPointOnSegment(p, a, b);
        closest = Closer(p, closest, ClosestPointOnSegment(p, b, c));
        return Closer(p, closest, ClosestPointOnSegment(p, c, a));
    }

    private static Vector3 Closer(Vector3 p, Vector3 first, Vector3 second) =>
        (p - second).LengthSquared() < (p - first).LengthSquared() ? second : first;

    private static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared > 0 ? Math.Clamp(Vector3.Dot(p - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return a + t * ab;
    }
}
