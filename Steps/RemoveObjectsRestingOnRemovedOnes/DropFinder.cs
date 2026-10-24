using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// How far another mod's object can move straight down before it meets what is below it: from each sample of its surface a ray
/// goes straight down to the nearest mesh of the world below it, or to the terrain; the smallest of these distances is the drop.
/// A sample at or below the terrain, or inside a mesh, gives no drop at all. The samples stand for the surface, so an edge of a
/// mesh below that passes between them may sink in slightly. An object rests on what is below it when it cannot move down by
/// more than the touch gap. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class DropFinder(SurfaceContacts surfaces, ITriangleMeshes cache, ITerrainHeights terrain, OtherModsObjectRules rules, float touchGap)
{
    private static readonly Vector3 Down = -Vector3.UnitZ;

    /// <summary>Whether the object, where it stands, rests on what is below it in the world.</summary>
    public bool Rests(OtherObject other, SupportWorld world, OtherObjectQueries queries) => FindDrop(other, world, queries) is { } drop && RestsAt(drop);

    /// <summary>Whether an object that can move down this far rests on what is below it.</summary>
    public bool RestsAt(float drop) => drop <= touchGap;

    /// <param name="other">A judged object, where it stands.</param>
    /// <returns>Null when nothing is below it within <see cref="OtherModsObjectsLimits.MaxPullDistance"/>.</returns>
    public float? FindDrop(OtherObject other, SupportWorld world, OtherObjectQueries queries)
    {
        var points = surfaces.SampleSurface(rules.MeshPathOf(other)).Points.Select(other.Transform.ToWorld).ToArray();
        var below = FindDistancesToTerrain(other.SpaceKey, points);
        foreach (var supporter in world.FindSupporters(other.SpaceKey, ColumnBelow(other), Supporter.Placed(other.Id), queries))
        {
            ShortenToMesh(points, supporter, below, queries.Query.Triangles);
        }
        var drop = below.Min();
        return drop <= OtherModsObjectsLimits.MaxPullDistance ? drop : null;
    }

    /// <summary>The object's bounds and the space straight below them, down to the pull limit.</summary>
    private Box ColumnBelow(OtherObject other)
    {
        var bounds = rules.BoxOf(other, other.Transform).WorldAabb(0f);
        return bounds with { Min = bounds.Min with { Z = bounds.Min.Z - OtherModsObjectsLimits.MaxPullDistance } };
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

    /// <summary>Lowers each point's distance to where its ray down meets the supporter's mesh, when that is nearer; to 0 inside the mesh.</summary>
    /// <remarks>
    /// The ray's direction is turned into the mesh's frame and divided by its scale, so a distance along it is in world units.
    /// A surface facing down within the touch gap of the point is the underside of something resting on the object, not
    /// what is below it, so the ray passes it.
    /// </remarks>
    private void ShortenToMesh(Vector3[] points, MeshSupporter supporter, float[] distances, List<int> scratch)
    {
        using var lease = cache.Acquire(supporter.MeshPath);
        if (lease.Value is not { } tree) return;

        var transform = supporter.Transform;
        var direction = transform.Rotation.TransformTransposed(Down) / transform.Scale;
        Func<MeshTriangle, float, bool> isBelow = (triangle, distance) => distance > touchGap || transform.Rotation.Transform(triangle.Normal).Z > 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var local = transform.ToLocal(points[i]);
            if (IsHeldInside(tree, local, direction, scratch)) distances[i] = 0f;
            else if (RayHit.TryFindNearest(tree, local, direction, isBelow, scratch, out var distance) && distance < distances[i]) distances[i] = distance;
        }
    }

    /// <summary>
    /// Whether the point lies inside the mesh, farther than the touch gap above the mesh's surface below it. A point just inside
    /// a mesh's underside is where the top of an object below touches that mesh, which does not hold the object up; from inside,
    /// the ray down would meet the mesh's underside instead of what is below.
    /// </summary>
    private bool IsHeldInside(MeshTriangleTree tree, Vector3 local, Vector3 direction, List<int> scratch) =>
        MeshContact.PointEnclosed(tree, local, scratch)
        && !(RayHit.TryFindNearest(tree, local, direction, static (_, _) => true, scratch, out var toSurfaceBelow) && toSurfaceBelow <= touchGap);
}
