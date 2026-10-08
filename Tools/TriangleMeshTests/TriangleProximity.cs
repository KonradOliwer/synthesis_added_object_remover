using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Exact triangle-to-triangle distance. When two triangles do not intersect, their closest points
/// are a vertex of one and a point of the other's face, or a point on an edge of each (Ericson,
/// Real-Time Collision Detection 5.1.10). When they do intersect, an edge of one crosses the other
/// (non-coplanar), or, when coplanar, two edges cross or a vertex lies inside the other triangle,
/// which the vertex and edge tests report at distance 0. Degenerate (zero-area) triangles are
/// covered by the vertex and edge tests.
/// </summary>
public static class TriangleProximity
{
    public static bool AreWithin(in MeshTriangle p, in MeshTriangle q, float distanceSquared) =>
        DistanceSquared(p, q, stopAtOrBelow: distanceSquared) <= distanceSquared;

    /// <summary>True minimum squared distance between two triangles (0 when they intersect, coplanar overlap included).</summary>
    public static float MinDistanceSquared(in MeshTriangle p, in MeshTriangle q) => DistanceSquared(p, q, stopAtOrBelow: 0f);

    /// <summary>The minimum squared distance, or any value at or below <paramref name="stopAtOrBelow"/> once one is found.</summary>
    private static float DistanceSquared(in MeshTriangle p, in MeshTriangle q, float stopAtOrBelow)
    {
        var minimum = new RunningMinimum(stopAtOrBelow);
        if (AddVertexToFace(p, q, ref minimum) || AddVertexToFace(q, p, ref minimum) || AddEdgeToEdge(p, q, ref minimum))
        {
            return minimum.Value;
        }
        return AnyEdgeCrosses(p, q) || AnyEdgeCrosses(q, p) ? 0f : minimum.Value;
    }

    /// <returns>True once the minimum is at or below the stop value.</returns>
    private static bool AddVertexToFace(in MeshTriangle vertices, in MeshTriangle face, ref RunningMinimum minimum) =>
        minimum.Add(TriangleDistance.DistanceSquaredToTriangle(vertices.A, face.A, face.B, face.C))
        || minimum.Add(TriangleDistance.DistanceSquaredToTriangle(vertices.B, face.A, face.B, face.C))
        || minimum.Add(TriangleDistance.DistanceSquaredToTriangle(vertices.C, face.A, face.B, face.C));

    /// <returns>True once the minimum is at or below the stop value.</returns>
    private static bool AddEdgeToEdge(in MeshTriangle p, in MeshTriangle q, ref RunningMinimum minimum) =>
        AddEdgeToTriangleEdges(p.A, p.B, q, ref minimum)
        || AddEdgeToTriangleEdges(p.B, p.C, q, ref minimum)
        || AddEdgeToTriangleEdges(p.C, p.A, q, ref minimum);

    private static bool AddEdgeToTriangleEdges(Vector3 start, Vector3 end, in MeshTriangle q, ref RunningMinimum minimum) =>
        minimum.Add(SegmentDistanceSquared(start, end, q.A, q.B))
        || minimum.Add(SegmentDistanceSquared(start, end, q.B, q.C))
        || minimum.Add(SegmentDistanceSquared(start, end, q.C, q.A));

    private static bool AnyEdgeCrosses(in MeshTriangle edges, in MeshTriangle face) =>
        SegmentCrossesTriangle(edges.A, edges.B, face)
        || SegmentCrossesTriangle(edges.B, edges.C, face)
        || SegmentCrossesTriangle(edges.C, edges.A, face);

    /// <summary>
    /// Whether the segment passes through the face: its ends lie on opposite sides of the face's
    /// plane (or one on it), and the point where it meets the plane lies in the face, edges
    /// included. The signed plane distances stay accurate at any angle, so a segment crossing at a
    /// very shallow angle is found too. A segment lying in the plane, and a degenerate face, never
    /// cross; the vertex and edge tests cover them.
    /// </summary>
    private static bool SegmentCrossesTriangle(Vector3 start, Vector3 end, in MeshTriangle face)
    {
        var normal = Vector3.Cross(face.B - face.A, face.C - face.A);
        var startSide = Vector3.Dot(normal, start - face.A);
        var endSide = Vector3.Dot(normal, end - face.A);
        if ((startSide > 0 && endSide > 0) || (startSide < 0 && endSide < 0) || startSide == endSide) return false;

        var crossing = start + (end - start) * (startSide / (startSide - endSide));
        return IsInFace(crossing, face, normal);
    }

    /// <summary>Whether a point in the face's plane lies in the face, edges included; false for NaN.</summary>
    private static bool IsInFace(Vector3 point, in MeshTriangle face, Vector3 normal) =>
        Vector3.Dot(normal, Vector3.Cross(face.B - face.A, point - face.A)) >= 0
        && Vector3.Dot(normal, Vector3.Cross(face.C - face.B, point - face.B)) >= 0
        && Vector3.Dot(normal, Vector3.Cross(face.A - face.C, point - face.C)) >= 0;

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

    private struct RunningMinimum(float stopAtOrBelow)
    {
        public float Value { get; private set; } = float.PositiveInfinity;

        /// <returns>True once the minimum is at or below the stop value.</returns>
        public bool Add(float distanceSquared)
        {
            if (distanceSquared < Value) Value = distanceSquared;
            return Value <= stopAtOrBelow;
        }
    }
}
