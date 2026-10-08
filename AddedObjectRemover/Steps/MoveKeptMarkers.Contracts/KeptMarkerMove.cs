using System.Numerics;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <summary>A kept marker moved out of the other mod's object it sat inside.</summary>
/// <param name="LeftHomeCell">No free spot was found in the marker's own exterior cell, so it was moved into a neighboring one.</param>
public sealed record KeptMarkerMove(LeftBehindCheck Evaluation, Vector3 From, Vector3 To, RelocationSurface Surface, bool LeftHomeCell)
{
    public float Distance => Vector3.Distance(From, To);
}
