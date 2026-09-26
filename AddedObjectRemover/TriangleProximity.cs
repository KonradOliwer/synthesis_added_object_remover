using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Exact "within distance" test for two triangles. When two triangles do not intersect, their
/// closest points are a vertex of one and a point of the other's face, or a point on an edge of
/// each (Ericson, Real-Time Collision Detection 5.1.10). When they do intersect, an edge of one
/// crosses the other (non-coplanar), or, when coplanar, two edges cross or a vertex lies inside
/// the other triangle, which the vertex and edge tests report at distance 0. Degenerate
/// (zero-area) triangles are covered by the vertex and edge tests.
/// </summary>
internal static class TriangleProximity
{
    public static bool AreWithin(in MeshTriangle p, in MeshTriangle q, float distanceSquared) =>
        AnyVertexWithin(p, q, distanceSquared)
        || AnyVertexWithin(q, p, distanceSquared)
        || AnyEdgePairWithin(p, q, distanceSquared)
        || AnyEdgeCrosses(p, q)
        || AnyEdgeCrosses(q, p);

    private static bool AnyVertexWithin(in MeshTriangle vertices, in MeshTriangle face, float distanceSquared) =>
        Geometry.DistanceSquaredToTriangle(vertices.A, face.A, face.B, face.C) <= distanceSquared
        || Geometry.DistanceSquaredToTriangle(vertices.B, face.A, face.B, face.C) <= distanceSquared
        || Geometry.DistanceSquaredToTriangle(vertices.C, face.A, face.B, face.C) <= distanceSquared;

    private static bool AnyEdgePairWithin(in MeshTriangle p, in MeshTriangle q, float distanceSquared) =>
        AnyEdgeWithin(p.A, p.B, q, distanceSquared)
        || AnyEdgeWithin(p.B, p.C, q, distanceSquared)
        || AnyEdgeWithin(p.C, p.A, q, distanceSquared);

    private static bool AnyEdgeWithin(Vector3 start, Vector3 end, in MeshTriangle q, float distanceSquared) =>
        SegmentDistanceSquared(start, end, q.A, q.B) <= distanceSquared
        || SegmentDistanceSquared(start, end, q.B, q.C) <= distanceSquared
        || SegmentDistanceSquared(start, end, q.C, q.A) <= distanceSquared;

    private static bool AnyEdgeCrosses(in MeshTriangle edges, in MeshTriangle face) =>
        SegmentCrossesTriangle(edges.A, edges.B, face)
        || SegmentCrossesTriangle(edges.B, edges.C, face)
        || SegmentCrossesTriangle(edges.C, edges.A, face);

    /// <summary>
    /// Möller-Trumbore restricted to the segment. A segment parallel to the plane or a degenerate
    /// face never crosses; those cases are decided by the vertex and edge distance tests.
    /// </summary>
    private static bool SegmentCrossesTriangle(Vector3 start, Vector3 end, in MeshTriangle face)
    {
        var direction = end - start;
        var edge1 = face.B - face.A;
        var edge2 = face.C - face.A;
        var h = Vector3.Cross(direction, edge2);
        var inverseDeterminant = 1f / Vector3.Dot(edge1, h);
        if (!float.IsFinite(inverseDeterminant)) return false;

        var fromA = start - face.A;
        var u = Vector3.Dot(fromA, h) * inverseDeterminant;
        if (u < 0 || u > 1) return false;

        var q = Vector3.Cross(fromA, edge1);
        var v = Vector3.Dot(direction, q) * inverseDeterminant;
        if (v < 0 || u + v > 1) return false;

        var t = Vector3.Dot(edge2, q) * inverseDeterminant;
        return t >= 0 && t <= 1;
    }

    /// <summary>Squared distance between segments p1-q1 and p2-q2 (Ericson 5.1.9), zero-length segments included.</summary>
    private static float SegmentDistanceSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = d1.LengthSquared();
        var e = d2.LengthSquared();
        var f = Vector3.Dot(d2, r);

        if (a <= 0 && e <= 0) return r.LengthSquared();

        float s, t;
        if (a <= 0)
        {
            s = 0;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= 0)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denominator = a * e - b * b;
                s = denominator > 0 ? Math.Clamp((b * f - c * e) / denominator, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        return (p1 + d1 * s - (p2 + d2 * t)).LengthSquared();
    }
}
