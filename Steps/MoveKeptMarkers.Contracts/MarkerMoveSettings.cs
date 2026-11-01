using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <param name="MaxDistance">Largest distance, in game units, a kept marker is moved.</param>
public sealed record MarkerMoveSettings(float MaxDistance)
{
    /// <summary>A marker is moved onto navmesh ground, so the navmeshes are read.</summary>
    public ScanNeeds Needs => new(Navmesh: true);
}
