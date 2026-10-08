using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <param name="LeftInPlace">Kept markers inside another mod's object for which no free spot was found.</param>
public sealed record MarkerMoves(IReadOnlyList<KeptMarkerMove> Moved, IReadOnlyList<LeftBehindCheck> LeftInPlace)
{
    public static MarkerMoves None { get; } = new([], []);
}
