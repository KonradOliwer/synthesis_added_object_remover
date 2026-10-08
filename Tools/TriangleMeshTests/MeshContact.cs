using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Contact questions between placed meshes, boxes and points. The callers decide what counts as
/// contact, so every tolerance and gap is passed in. The other classes of this tool are helpers of
/// these operations.
/// </summary>
public static class MeshContact
{
    /// <summary>Whether a triangle of the placed mesh overlaps the box (touching counts).</summary>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool BoxOverlapsMesh(
        MeshTriangleTree tree,
        PlacedTransform treeTransform,
        Box box,
        PlacedTransform boxTransform,
        List<int> scratch)
    {
        var boxInTree = RelativeTransform.Create(from: boxTransform, to: treeTransform).ApplyToBox(box);
        if (!boxInTree.Overlaps(tree.Bounds)) return false;

        var toBox = RelativeTransform.Create(from: treeTransform, to: boxTransform);
        tree.CollectLeafTriangles(boxInTree, scratch);
        foreach (var triangle in scratch)
        {
            if (TriangleBoxOverlap.Overlaps(tree.GetTriangle(triangle).Transformed(toBox), box)) return true;
        }
        return false;
    }

    /// <summary>Whether the mesh surrounds the point (see <see cref="SurroundingRayTest"/>); open meshes included.</summary>
    /// <param name="localPoint">The point in the mesh's local frame.</param>
    /// <param name="rotation">The placed mesh's rotation, turning local directions into world ones.</param>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool PointInside(MeshTriangleTree tree, Vector3 localPoint, Mat3 rotation, List<int> scratch) =>
        SurroundingRayTest.IsSurrounded(tree, localPoint, rotation, scratch);

    /// <summary>Whether a triangle of one placed mesh comes within the tolerance (world units) of a triangle of the other.</summary>
    public static bool SurfacesTouch(
        MeshTriangleTree first,
        PlacedTransform firstTransform,
        MeshTriangleTree second,
        PlacedTransform secondTransform,
        float tolerance,
        TouchScratch scratch) =>
        MeshTouchTest.Touches(first, firstTransform, second, secondTransform, tolerance, scratch);

    /// <summary>Minimum surface distance in world units, only exact for meshes that touch within the tolerance; NaN when no triangles are near.</summary>
    public static float MinSurfaceDistance(
        MeshTriangleTree first,
        PlacedTransform firstTransform,
        MeshTriangleTree second,
        PlacedTransform secondTransform,
        float tolerance,
        TouchScratch scratch) =>
        MeshTouchTest.MinSurfaceDistance(first, firstTransform, second, secondTransform, tolerance, scratch);

    /// <summary>Whether lines through the point along each of the mesh's axes all cross it an odd number of times on both sides (see <see cref="PointContactTest"/>).</summary>
    /// <param name="localPoint">The point in the mesh's local frame.</param>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool PointEnclosed(MeshTriangleTree tree, Vector3 localPoint, List<int> scratch) =>
        PointContactTest.IsEnclosed(tree, localPoint, scratch);

    /// <summary>
    /// Whether the meshes' surfaces come within the tolerance, or the first mesh is closed and
    /// surrounds a vertex of some part of the second. Once no triangles touch, a closed
    /// mesh holds each part wholly or not at all, so one vertex per part decides; an open mesh
    /// surrounds nothing.
    /// </summary>
    public static bool TouchesOrEnclosesPart(
        MeshTriangleTree first,
        PlacedTransform firstTransform,
        MeshTriangleTree second,
        PlacedTransform secondTransform,
        float tolerance,
        TouchScratch scratch) =>
        SurfacesTouch(first, firstTransform, second, secondTransform, tolerance, scratch)
        || (first.IsClosed && EnclosesVertexOfAnyPart(first, firstTransform, second, secondTransform, scratch));

    /// <summary>Per sample (in <paramref name="samplesTransform"/>'s frame): whether it is within the gap of the mesh's surface or enclosed by it.</summary>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    public static bool[] SamplesTouching(
        Vector3[] samples,
        PlacedTransform samplesTransform,
        MeshTriangleTree tree,
        PlacedTransform treeTransform,
        float gap,
        List<int> scratch)
    {
        var touching = new bool[samples.Length];
        var toTree = RelativeTransform.Create(from: samplesTransform, to: treeTransform);
        var localGap = gap / treeTransform.Scale;
        var region = tree.Bounds.Grown(localGap);
        for (var i = 0; i < samples.Length; i++)
        {
            var point = toTree.Apply(samples[i]);
            touching[i] = region.Contains(point) && PointContactTest.IsInContact(tree, point, localGap, scratch);
        }
        return touching;
    }

    private static bool EnclosesVertexOfAnyPart(
        MeshTriangleTree enclosing,
        PlacedTransform enclosingTransform,
        MeshTriangleTree other,
        PlacedTransform otherTransform,
        TouchScratch scratch)
    {
        foreach (var firstTriangle in other.PartFirstTriangles)
        {
            var vertex = otherTransform.ToWorld(other.GetTriangle(firstTriangle).A);
            if (PointInside(enclosing, enclosingTransform.ToLocal(vertex), enclosingTransform.Rotation, scratch.NearbyTriangles)) return true;
        }
        return false;
    }
}
