using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Cache C14: the winning navmesh triangles (world space) of each space, each bucket read and indexed once. Thread-safe.</summary>
public interface INavmeshes
{
    /// <summary>
    /// The navmesh point nearest to <paramref name="point"/>, within <paramref name="maxDistance"/>,
    /// that <paramref name="isFree"/> accepts.
    /// </summary>
    bool TryFindNearestFreePoint(RecordKey spaceKey, Vector3 point, float maxDistance, Func<Vector3, bool> isFree, out Vector3 found);
}
