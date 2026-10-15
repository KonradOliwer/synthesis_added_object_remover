namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <summary>The part of the marker-move step that records its unexpected errors; it has no subject.</summary>
public static class MarkerMoveParts
{
    private const string Owner = "MoveKeptMarkers";

    /// <summary>Moving the kept markers.</summary>
    public static readonly PartKey MarkerMoves = new(Owner, nameof(MarkerMoves));
}
