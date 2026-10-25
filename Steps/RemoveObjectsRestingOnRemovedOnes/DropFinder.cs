using System.Numerics;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// How far an object can move straight down before it meets what is below it: from each sample of its surface a ray goes straight
/// down to the nearest mesh of the world below it, or to the terrain; the smallest of these distances is the drop. A sample at
/// or below the terrain, or sunk into a closed mesh, gives no drop at all. The samples stand for the surface, so an edge of a
/// mesh below that passes between them may sink in slightly. An object rests on what is below it when it cannot move down by
/// more than the touch gap. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class DropFinder(SurfaceContacts surfaces, ITriangleMeshes cache, ITerrainHeights terrain, float touchGap)
{
    private static readonly Vector3 Down = -Vector3.UnitZ;

    /// <summary>
    /// World units a mesh's top may lie below the deepest point a resting sample's ray reaches and still be asked. Far above the
    /// float rounding of positions and ray distances anywhere in a world, so a mesh skipped for lying lower never holds a sample.
    /// </summary>
    internal const float RoundingMargin = 8f;

    /// <summary>Whether the object, where it stands, rests on what is below it in the world.</summary>
    /// <remarks>
    /// The answer of <see cref="FindDrop"/> and <see cref="RestsAt"/> without measuring the whole drop, as the touch gap is below
    /// the pull limit: it stops at the first sample within the gap of the terrain or of a mesh, and skips each mesh whose top lies
    /// too low for any sample's ray to reach. Positions and mesh vertices are finite, so no distance is NaN.
    /// </remarks>
    public bool Rests(StandingObject faller, SupportWorld world, OtherObjectQueries queries)
    {
        var points = SampleSurfaceInWorld(faller);
        if (FindDistancesToTerrain(faller.Space, points).Any(RestsAt)) return true;

        var bounds = faller.Box.WorldAabb(0f);
        var lowestReach = points.Min(point => point.Z) - touchGap - RoundingMargin;
        return FindSupportersBelow(faller, bounds, world, queries)
            .Any(supporter => HoldsUpAnyPoint(points, bounds.Center.Z, lowestReach, supporter, queries.Query.Triangles));
    }

    /// <summary>Whether an object that can move down this far rests on what is below it.</summary>
    public bool RestsAt(float drop) => drop <= touchGap;

    /// <returns>Null when nothing is below it within <see cref="OtherModsObjectsLimits.MaxPullDistance"/>.</returns>
    public float? FindDrop(StandingObject faller, SupportWorld world, OtherObjectQueries queries)
    {
        var bounds = faller.Box.WorldAabb(0f);
        var points = SampleSurfaceInWorld(faller);
        var below = FindDistancesToTerrain(faller.Space, points);
        foreach (var supporter in FindSupportersBelow(faller, bounds, world, queries))
        {
            ShortenToMesh(points, bounds.Center.Z, supporter, below, queries.Query.Triangles);
        }
        var drop = below.Min();
        return drop <= OtherModsObjectsLimits.MaxPullDistance ? drop : null;
    }

    private Vector3[] SampleSurfaceInWorld(StandingObject faller) => [.. surfaces.SampleSurface(faller.MeshPath).Points.Select(faller.Transform.ToWorld)];

    /// <summary>The supporters within the object's bounds reaching down by the pull limit.</summary>
    /// <param name="bounds">The object's world bounds.</param>
    private static List<MeshSupporter> FindSupportersBelow(StandingObject faller, Box bounds, SupportWorld world, OtherObjectQueries queries)
    {
        var columnBelow = bounds with { Min = bounds.Min with { Z = bounds.Min.Z - OtherModsObjectsLimits.MaxPullDistance } };
        return world.FindSupporters(faller.Space, columnBelow, faller.Self, queries);
    }

    /// <returns>Per point: its height above the terrain, 0 at or below it, infinity where the space has no terrain.</returns>
    private float[] FindDistancesToTerrain(RecordKey space, Vector3[] points)
    {
        var distances = new float[points.Length];
        var hasTerrain = terrain.HasTerrain(space);
        for (var i = 0; i < points.Length; i++)
        {
            distances[i] = hasTerrain && terrain.TryGetHeight(space, new Vector2(points[i].X, points[i].Y), out var height)
                ? MathF.Max(points[i].Z - height, 0f)
                : float.PositiveInfinity;
        }
        return distances;
    }

    /// <summary>Lowers each point's distance to where its ray down meets the supporter's mesh, when that is nearer; to 0 when sunk into the mesh.</summary>
    /// <param name="middleHeight">The height of the middle of the object's bounds.</param>
    private void ShortenToMesh(Vector3[] points, float middleHeight, MeshSupporter supporter, float[] distances, List<int> scratch)
    {
        using var lease = cache.Acquire(supporter.MeshPath);
        if (lease.Value is not { } tree) return;

        var transform = supporter.Transform;
        var (direction, isBelow) = CreateRayDown(transform);
        for (var i = 0; i < points.Length; i++)
        {
            var local = transform.ToLocal(points[i]);
            if (IsSunkInto(tree, local, direction, inLowerHalf: points[i].Z <= middleHeight, scratch)) distances[i] = 0f;
            else if (RayHit.TryFindNearest(tree, local, direction, isBelow, scratch, out var distance) && distance < distances[i]) distances[i] = distance;
        }
    }

    /// <summary>Whether a point is sunk into the supporter's mesh, or its ray down meets the mesh within the touch gap.</summary>
    /// <param name="middleHeight">The height of the middle of the object's bounds.</param>
    /// <param name="lowestReach">A mesh whose top lies below it can hold up no point.</param>
    private bool HoldsUpAnyPoint(Vector3[] points, float middleHeight, float lowestReach, MeshSupporter supporter, List<int> scratch)
    {
        using var lease = cache.Acquire(supporter.MeshPath);
        if (lease.Value is not { } tree || TopOf(tree, supporter) < lowestReach) return false;

        var transform = supporter.Transform;
        var (direction, isBelow) = CreateRayDown(transform);
        return points.Any(point =>
        {
            var local = transform.ToLocal(point);
            return IsSunkInto(tree, local, direction, inLowerHalf: point.Z <= middleHeight, scratch)
                || (RayHit.TryFindNearest(tree, local, direction, isBelow, scratch, out var distance) && RestsAt(distance));
        });
    }

    private static float TopOf(MeshTriangleTree tree, MeshSupporter supporter) => OrientedBox.FromLocal(tree.Bounds, supporter.Transform).WorldAabb(0f).Max.Z;

    /// <summary>The ray straight down in the mesh's frame, and which of its hits are below the object.</summary>
    /// <remarks>
    /// The ray's direction is turned into the mesh's frame and divided by its scale, so a distance along it is in world units.
    /// A surface facing down within the touch gap of the point is the underside of something resting on the object, not
    /// what is below it, so the ray passes it.
    /// </remarks>
    private (Vector3 Direction, Func<MeshTriangle, float, bool> IsBelow) CreateRayDown(PlacedTransform transform) =>
        (transform.Rotation.TransformTransposed(Down) / transform.Scale,
            (triangle, distance) => distance > touchGap || transform.Rotation.Transform(triangle.Normal).Z > 0f);

    /// <summary>
    /// Whether the point lies inside the closed mesh and that holds the object up: the point is in the lower half of the object,
    /// or farther than the touch gap above the mesh's surface below it. A point of the upper half just inside a mesh's underside
    /// is where the top of the object touches something above it, which does not hold it up. The lower half counts however thin
    /// the mesh is, so an object sunk into a board thinner than the gap rests on it; an open mesh encloses nothing.
    /// </summary>
    private bool IsSunkInto(MeshTriangleTree tree, Vector3 local, Vector3 direction, bool inLowerHalf, List<int> scratch) =>
        tree.IsClosed
        && MeshContact.PointEnclosed(tree, local, scratch)
        && (inLowerHalf || !(RayHit.TryFindNearest(tree, local, direction, static (_, _) => true, scratch, out var toSurfaceBelow) && toSurfaceBelow <= touchGap));
}
