namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <param name="MaxDistance">Largest distance, in game units, a kept marker is moved.</param>
public sealed record MarkerMoveSettings(float MaxDistance);
