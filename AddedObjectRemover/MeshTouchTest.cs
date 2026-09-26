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
    public static bool Touches(
        MeshTriangleTree first,
        PlacedTransform firstTransform,
        MeshTriangleTree second,
        PlacedTransform secondTransform,
        float tolerance,
        TouchScratch scratch)
    {
        var frames = PairFrames.Create(first, firstTransform, second, secondTransform, tolerance);
        if (!frames.OverlapRegion.Overlaps(frames.Walked.Bounds)) return false;

        frames.Walked.CollectLeafTriangles(frames.OverlapRegion, scratch.WalkedTriangles);
        foreach (var triangle in scratch.WalkedTriangles)
        {
            if (IsNearAnyTriangle(frames.Lookup, frames.PlaceInLookup(triangle), frames.LookupTolerance, scratch)) return true;
        }
        return false;
    }

    /// <summary>
    /// Minimum surface distance (world units) between the two meshes' triangles within the same
    /// overlap region as <see cref="Touches"/>, or NaN when the region holds no triangles. It is
    /// the true minimum only for meshes that touch: only then does the region hold the closest pair.
    /// </summary>
    public static float MinSurfaceDistance(
        MeshTriangleTree first,
        PlacedTransform firstTransform,
        MeshTriangleTree second,
        PlacedTransform secondTransform,
        float tolerance,
        TouchScratch scratch)
    {
        var frames = PairFrames.Create(first, firstTransform, second, secondTransform, tolerance);
        frames.Walked.CollectLeafTriangles(frames.OverlapRegion, scratch.WalkedTriangles);
        var minDistanceSquared = float.PositiveInfinity;
        foreach (var triangle in scratch.WalkedTriangles)
        {
            var placed = frames.PlaceInLookup(triangle);
            frames.Lookup.CollectLeafTriangles(placed.Bounds.Grown(frames.LookupTolerance), scratch.NearbyTriangles);
            foreach (var nearbyIndex in scratch.NearbyTriangles)
            {
                minDistanceSquared = MathF.Min(
                    minDistanceSquared, TriangleProximity.MinDistanceSquared(placed, frames.Lookup.GetTriangle(nearbyIndex)));
            }
        }
        return float.IsPositiveInfinity(minDistanceSquared) ? float.NaN : frames.ToWorldDistance(MathF.Sqrt(minDistanceSquared));
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
    /// The walked mesh, the lookup mesh, the overlap region in the walked mesh's frame, and the
    /// walked-to-lookup transform with the tolerance in lookup units.
    /// </summary>
    private readonly record struct PairFrames(
        MeshTriangleTree Walked,
        MeshTriangleTree Lookup,
        Box OverlapRegion,
        RelativeTransform ToLookup,
        float LookupScale,
        float LookupTolerance)
    {
        /// <remarks>The mesh with fewer triangles is walked; ties walk <paramref name="first"/>, so the result does not depend on scheduling.</remarks>
        public static PairFrames Create(
            MeshTriangleTree first,
            PlacedTransform firstTransform,
            MeshTriangleTree second,
            PlacedTransform secondTransform,
            float tolerance) =>
            first.TriangleCount <= second.TriangleCount
                ? CreateWalking(walked: first, firstTransform, lookup: second, secondTransform, tolerance)
                : CreateWalking(walked: second, secondTransform, lookup: first, firstTransform, tolerance);

        private static PairFrames CreateWalking(
            MeshTriangleTree walked,
            PlacedTransform walkedTransform,
            MeshTriangleTree lookup,
            PlacedTransform lookupTransform,
            float tolerance) => new(
            walked,
            lookup,
            RelativeTransform.Create(from: lookupTransform, to: walkedTransform)
                .ApplyToBox(lookup.Bounds)
                .Grown(tolerance / walkedTransform.Scale),
            RelativeTransform.Create(from: walkedTransform, to: lookupTransform),
            lookupTransform.Scale,
            tolerance / lookupTransform.Scale);

        public MeshTriangle PlaceInLookup(int walkedTriangle) => ToLookup.Apply(Walked.GetTriangle(walkedTriangle));

        public float ToWorldDistance(float lookupDistance) => lookupDistance * LookupScale;
    }
}
