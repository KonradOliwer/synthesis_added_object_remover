using System.Numerics;

namespace AddedObjectRemover;

/// <summary>The winning navmesh triangles (world space) of each space, each bucket read and indexed once. Thread-safe.</summary>
public interface INavmeshes
{
    /// <summary>
    /// The navmesh point nearest to <paramref name="point"/>, within <paramref name="maxDistance"/>,
    /// that <paramref name="isFree"/> accepts.
    /// </summary>
    bool TryFindNearestFreePoint(RecordKey spaceKey, Vector3 point, float maxDistance, Func<Vector3, bool> isFree, out Vector3 found);

    /// <summary>Whether a triangle of an included navmesh crosses the box or lies inside it; touching counts, and the box is not grown.</summary>
    bool AnyTriangleIn(RecordKey spaceKey, OrientedBox box, Func<RecordKey, bool> include);
}
