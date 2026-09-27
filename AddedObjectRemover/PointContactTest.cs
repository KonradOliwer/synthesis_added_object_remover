using System.Diagnostics;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a point (in a mesh's local frame) is in contact with that mesh: within the tolerance of
/// one of its triangles, or enclosed by it. A point counts as enclosed when lines through it along
/// each of the mesh's local axes Z, X and Y all cross the mesh an odd number of times both before
/// and after it. This is exact for closed meshes. An open mesh encloses nothing unless it is closed
/// along all three lines, so layered open meshes (floor and ceiling of one room piece, storeys of
/// a building shell) do not; a mesh closed on every side, such as a room piece with walls, floor
/// and ceiling, still encloses what is inside it.
/// </summary>
internal static class PointContactTest
{
    private enum LineAxis { Z, X, Y }

    private static readonly LineAxis[] LineAxes = [LineAxis.Z, LineAxis.X, LineAxis.Y];

    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool IsInContact(MeshTriangleTree tree, Vector3 point, float tolerance, List<int> scratch) =>
        IsNearSurface(tree, point, tolerance, scratch) || IsEnclosed(tree, point, scratch);

    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool IsEnclosed(MeshTriangleTree tree, Vector3 point, List<int> scratch)
    {
        if (!tree.Bounds.Contains(point)) return false;
        foreach (var axis in LineAxes)
        {
            if (!IsInsideAlong(tree, point, axis, scratch)) return false;
        }
        return true;
    }

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

    private static bool IsInsideAlong(MeshTriangleTree tree, Vector3 point, LineAxis axis, List<int> scratch)
    {
        tree.CollectLeafTriangles(LineThroughBounds(point, tree.Bounds, axis), scratch);
        var framedPoint = ToLineFrame(point, axis);
        var after = 0;
        var before = 0;
        foreach (var index in scratch)
        {
            var triangle = tree.GetTriangle(index);
            var framed = new MeshTriangle(ToLineFrame(triangle.A, axis), ToLineFrame(triangle.B, axis), ToLineFrame(triangle.C, axis));
            if (!TryGetLineCrossing(framed, framedPoint, out var crossing)) continue;
            if (crossing > framedPoint.Z) after++;
            else if (crossing < framedPoint.Z) before++;
        }
        return after % 2 == 1 && before % 2 == 1;
    }

    /// <summary>The segment of the line through the point along the axis that lies inside the bounds, as a degenerate box.</summary>
    private static Box LineThroughBounds(Vector3 point, Box bounds, LineAxis axis) => axis switch
    {
        LineAxis.Z => new Box(point with { Z = bounds.Min.Z }, point with { Z = bounds.Max.Z }),
        LineAxis.X => new Box(point with { X = bounds.Min.X }, point with { X = bounds.Max.X }),
        LineAxis.Y => new Box(point with { Y = bounds.Min.Y }, point with { Y = bounds.Max.Y }),
        _ => throw new UnreachableException($"Unknown line axis {axis}."),
    };

    /// <summary>Permutes the coordinates so the line axis becomes Z.</summary>
    private static Vector3 ToLineFrame(Vector3 v, LineAxis axis) => axis switch
    {
        LineAxis.Z => v,
        LineAxis.X => new Vector3(v.Y, v.Z, v.X),
        LineAxis.Y => new Vector3(v.Z, v.X, v.Y),
        _ => throw new UnreachableException($"Unknown line axis {axis}."),
    };

    /// <summary>
    /// Z at which the line along Z through the point crosses the triangle. A line through an edge
    /// or corner shared by several triangles must be counted once, so the projected triangle is
    /// turned counter-clockwise and a point exactly on an edge belongs to it only when that edge
    /// is a top or left edge (the rasterization fill rule). Two triangles on opposite sides of a
    /// shared edge then count it exactly once, and two folding back on the same side zero or two
    /// times, which keeps the parity. Triangles seen edge-on are never crossed.
    /// </summary>
    private static bool TryGetLineCrossing(MeshTriangle triangle, Vector3 point, out float z)
    {
        z = 0;
        var (a, b, c) = (triangle.A, triangle.B, triangle.C);
        var orientation = EdgeFunction(a, b, c);
        if (orientation == 0) return false;
        if (orientation < 0) (b, c) = (c, b);

        var edgeBc = EdgeFunction(b, c, point);
        var edgeCa = EdgeFunction(c, a, point);
        var edgeAb = EdgeFunction(a, b, point);
        if (!Covers(edgeBc, b, c) || !Covers(edgeCa, c, a) || !Covers(edgeAb, a, b)) return false;

        // Each edge function is twice the signed area of the sub-triangle opposite a corner, so it is that corner's barycentric weight.
        var twiceArea = edgeBc + edgeCa + edgeAb;
        z = (float)((edgeBc * a.Z + edgeCa * b.Z + edgeAb * c.Z) / twiceArea);
        return true;
    }

    /// <summary>Whether a point with this edge function value belongs to the counter-clockwise triangle, by the top-left rule on the edge.</summary>
    private static bool Covers(double edgeFunction, Vector3 start, Vector3 end) =>
        edgeFunction > 0 || (edgeFunction == 0 && IsTopOrLeftEdge(start, end));

    /// <summary>A left edge runs downwards, a top edge is horizontal and runs leftwards (counter-clockwise, Y up).</summary>
    private static bool IsTopOrLeftEdge(Vector3 start, Vector3 end) =>
        end.Y < start.Y || (end.Y == start.Y && end.X < start.X);

    /// <summary>
    /// Z of (end - start) x (point - start) in the XY plane. Always evaluated from the edge's
    /// lower endpoint (by X, then Y), so the two triangles sharing an edge get exactly opposite values.
    /// </summary>
    private static double EdgeFunction(Vector3 start, Vector3 end, Vector3 point) =>
        start.X < end.X || (start.X == end.X && start.Y <= end.Y)
            ? Cross2D(start, end, point)
            : -Cross2D(end, start, point);

    private static double Cross2D(Vector3 start, Vector3 end, Vector3 point) =>
        ((double)end.X - start.X) * ((double)point.Y - start.Y) - ((double)end.Y - start.Y) * ((double)point.X - start.X);
}
