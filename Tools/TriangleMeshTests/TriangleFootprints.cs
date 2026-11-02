using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Triangles seen from above (X and Y only).</summary>
public static class TriangleFootprints
{
    /// <summary>Whether the two triangles share an area seen from above; touching along an edge or at a corner is no overlap.</summary>
    /// <remarks>
    /// Two convex shapes with area are apart exactly when one of their edges' normals separates them. A triangle without area seen
    /// from above overlaps nothing; it is ruled out first, since no edge normal separates a sliver that crosses another triangle.
    /// </remarks>
    public static bool Overlap(MeshTriangle first, MeshTriangle second)
    {
        Vector2[] one = [ToXY(first.A), ToXY(first.B), ToXY(first.C)];
        Vector2[] other = [ToXY(second.A), ToXY(second.B), ToXY(second.C)];
        return HasArea(one) && HasArea(other) && !HasSeparatingEdge(one, other) && !HasSeparatingEdge(other, one);
    }

    private static Vector2 ToXY(Vector3 point) => new(point.X, point.Y);

    /// <summary>Whether the corners do not lie on one line: twice the triangle's signed area is not zero.</summary>
    private static bool HasArea(Vector2[] corners)
    {
        var (first, second) = (corners[1] - corners[0], corners[2] - corners[0]);
        return first.X * second.Y - first.Y * second.X != 0f;
    }

    private static bool HasSeparatingEdge(Vector2[] edgesOf, Vector2[] other)
    {
        for (var corner = 0; corner < MeshTriangle.CornerCount; corner++)
        {
            var edge = edgesOf[(corner + 1) % MeshTriangle.CornerCount] - edgesOf[corner];
            if (Separates(new Vector2(-edge.Y, edge.X), edgesOf, other)) return true;
        }
        return false;
    }

    private static bool Separates(Vector2 axis, Vector2[] one, Vector2[] other)
    {
        var (oneMin, oneMax) = Project(axis, one);
        var (otherMin, otherMax) = Project(axis, other);
        return oneMax <= otherMin || otherMax <= oneMin;
    }

    private static (float Min, float Max) Project(Vector2 axis, Vector2[] corners)
    {
        var values = corners.Select(corner => Vector2.Dot(axis, corner)).ToArray();
        return (values.Min(), values.Max());
    }
}
