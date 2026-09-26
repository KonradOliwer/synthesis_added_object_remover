using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Reusable per-thread buffers and counters of <see cref="MeshTouchTest"/>.</summary>
internal sealed class TouchScratch
{
    public List<int> WalkedTriangles { get; } = [];
    public List<int> NearbyTriangles { get; } = [];
    public long TrianglePairsTested { get; set; }
}

/// <summary>
/// Exact touch test of two placed meshes: true if some triangle of one comes within the tolerance
/// (world units) of some triangle of the other. Only the walked mesh's triangles inside the
/// overlap region (the other mesh's bounds, placed into the walked mesh's frame and grown by the
/// tolerance) are visited; each is placed into the other mesh's frame and tested exactly against
/// the other mesh's triangles near it (<see cref="TriangleProximity"/>).
/// </summary>
internal static class MeshTouchTest
{
    /// <remarks>The mesh with fewer triangles is walked; ties walk <paramref name="first"/>, so the result does not depend on scheduling.</remarks>
    public static bool Touches(
        MeshTriangleTree first,
        PlacedTransform firstTransform,
        MeshTriangleTree second,
        PlacedTransform secondTransform,
        float tolerance,
        TouchScratch scratch) =>
        first.TriangleCount <= second.TriangleCount
            ? WalkAndLookUp(walked: first, firstTransform, lookup: second, secondTransform, tolerance, scratch)
            : WalkAndLookUp(walked: second, secondTransform, lookup: first, firstTransform, tolerance, scratch);

    /// <summary>
    /// True minimum surface distance (world units) between the two meshes, searched within the same
    /// tolerance-grown overlap region as <see cref="Touches"/>. For touch diagnostics only: called
    /// only on a pair already known to touch, so that region is guaranteed to hold the closest
    /// triangle pair; never called from the hot narrow-phase path.
    /// </summary>
    public static float MinSurfaceDistance(
        MeshTriangleTree first,
        PlacedTransform firstTransform,
        MeshTriangleTree second,
        PlacedTransform secondTransform,
        float tolerance,
        TouchScratch scratch) =>
        first.TriangleCount <= second.TriangleCount
            ? WalkMinDistance(walked: first, firstTransform, lookup: second, secondTransform, tolerance, scratch)
            : WalkMinDistance(walked: second, secondTransform, lookup: first, firstTransform, tolerance, scratch);

    private static float WalkMinDistance(
        MeshTriangleTree walked,
        PlacedTransform walkedTransform,
        MeshTriangleTree lookup,
        PlacedTransform lookupTransform,
        float tolerance,
        TouchScratch scratch)
    {
        var overlapRegion = RelativeTransform.Create(from: lookupTransform, to: walkedTransform)
            .ApplyToBox(lookup.Bounds)
            .Grown(tolerance / walkedTransform.Scale);

        walked.CollectLeafTriangles(overlapRegion, scratch.WalkedTriangles);
        var toLookup = RelativeTransform.Create(from: walkedTransform, to: lookupTransform);
        var lookupTolerance = tolerance / lookupTransform.Scale;
        var minDistanceSquared = float.PositiveInfinity;
        foreach (var triangle in scratch.WalkedTriangles)
        {
            var placed = toLookup.Apply(walked.GetTriangle(triangle));
            var reach = placed.Bounds.Grown(lookupTolerance);
            lookup.CollectLeafTriangles(reach, scratch.NearbyTriangles);
            foreach (var nearbyIndex in scratch.NearbyTriangles)
            {
                var distanceSquared = TriangleProximity.MinDistanceSquared(placed, lookup.GetTriangle(nearbyIndex));
                if (distanceSquared < minDistanceSquared) minDistanceSquared = distanceSquared;
            }
        }
        // Distances above were computed in the lookup mesh's local units; scale back to world units.
        return float.IsPositiveInfinity(minDistanceSquared) ? float.NaN : MathF.Sqrt(minDistanceSquared) * lookupTransform.Scale;
    }

    private static bool WalkAndLookUp(
        MeshTriangleTree walked,
        PlacedTransform walkedTransform,
        MeshTriangleTree lookup,
        PlacedTransform lookupTransform,
        float tolerance,
        TouchScratch scratch)
    {
        var overlapRegion = RelativeTransform.Create(from: lookupTransform, to: walkedTransform)
            .ApplyToBox(lookup.Bounds)
            .Grown(tolerance / walkedTransform.Scale);
        if (!overlapRegion.Overlaps(walked.Bounds)) return false;

        walked.CollectLeafTriangles(overlapRegion, scratch.WalkedTriangles);
        var toLookup = RelativeTransform.Create(from: walkedTransform, to: lookupTransform);
        var lookupTolerance = tolerance / lookupTransform.Scale;
        foreach (var triangle in scratch.WalkedTriangles)
        {
            if (IsNearAnyTriangle(lookup, toLookup.Apply(walked.GetTriangle(triangle)), lookupTolerance, scratch)) return true;
        }
        return false;
    }

    /// <param name="triangle">Already in <paramref name="lookup"/>'s frame.</param>
    private static bool IsNearAnyTriangle(MeshTriangleTree lookup, MeshTriangle triangle, float tolerance, TouchScratch scratch)
    {
        var reach = triangle.Bounds.Grown(tolerance);
        if (!reach.Overlaps(lookup.Bounds)) return false;

        lookup.CollectLeafTriangles(reach, scratch.NearbyTriangles);
        var toleranceSquared = tolerance * tolerance;
        foreach (var nearbyIndex in scratch.NearbyTriangles)
        {
            var nearby = lookup.GetTriangle(nearbyIndex);
            if (!nearby.Bounds.Overlaps(reach)) continue;

            scratch.TrianglePairsTested++;
            if (TriangleProximity.AreWithin(triangle, nearby, toleranceSquared)) return true;
        }
        return false;
    }

    /// <summary>
    /// Mesh-local of one reference -> mesh-local of another: x_to = R_to^T * (pos_from + R_from * (s_from * x) - pos_to) / s_to,
    /// computed as Rotation * x * Ratio + Translation.
    /// </summary>
    private readonly record struct RelativeTransform(Mat3 Rotation, float Ratio, Vector3 Translation)
    {
        public static RelativeTransform Create(PlacedTransform from, PlacedTransform to) => new(
            to.Rotation.Transposed() * from.Rotation,
            from.Scale / to.Scale,
            to.Rotation.TransformTransposed(from.Position - to.Position) / to.Scale);

        public Vector3 Apply(Vector3 v) => Rotation.Transform(v) * Ratio + Translation;

        public MeshTriangle Apply(MeshTriangle triangle) => new(Apply(triangle.A), Apply(triangle.B), Apply(triangle.C));

        /// <summary>AABB enclosing the transformed box.</summary>
        public Box ApplyToBox(Box box)
        {
            var center = Apply(box.Center);
            var halfExtents = Rotation.AbsTransform(box.Size * 0.5f) * Ratio;
            return new Box(center - halfExtents, center + halfExtents);
        }
    }
}
