using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <summary>What only the report needs about one move: the evaluation behind it and where it landed.</summary>
/// <param name="LeftHomeCell">No free spot was found in the marker's own exterior cell, so it was moved into a neighboring one.</param>
public sealed record MarkerMoveDetail(LeftBehindCheck Evaluation, RelocationSurface Surface, bool LeftHomeCell);
