namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The navmesh triangles as they will stand once the patch is written. Thread-safe.</summary>
/// <param name="winning">The winning navmeshes.</param>
/// <param name="includesWinning">Whether a winning navmesh keeps its triangles.</param>
/// <param name="goingBack">The earlier triangles that come back; null when none do.</param>
internal sealed class NavmeshesAsTheyWillStand(INavmeshes winning, Func<RecordKey, bool> includesWinning, INavmeshes? goingBack)
{
    /// <summary>Whether a triangle crosses the box or lies inside it; touching counts.</summary>
    public bool AnyTriangleIn(RecordKey spaceKey, OrientedBox box) =>
        winning.AnyTriangleIn(spaceKey, box, includesWinning) || goingBack?.AnyTriangleIn(spaceKey, box, _ => true) == true;
}
