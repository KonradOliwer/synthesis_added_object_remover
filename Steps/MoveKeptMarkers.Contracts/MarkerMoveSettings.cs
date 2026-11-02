using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <param name="MaxDistance">Largest distance, in game units, a kept marker is moved.</param>
/// <param name="Runs">Whether the user switched the moves on and the invisible left-behind step, whose kept markers they move, is on; otherwise no marker is a candidate and the step is switched off.</param>
public sealed record MarkerMoveSettings(float MaxDistance, bool Runs)
{
    /// <summary>A marker is moved onto navmesh ground, so the navmeshes are read when the step runs.</summary>
    public ScanNeeds Needs => new(Navmesh: Runs);
}
