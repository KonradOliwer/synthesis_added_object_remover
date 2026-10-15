using System.Numerics;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>A kept marker's move together with the report-only facts about it.</summary>
internal sealed record ReportedMove(MarkerMove Move, MarkerMoveDetail Detail)
{
    public float Distance => Vector3.Distance(Move.From, Move.To);

    public static IEnumerable<ReportedMove> Of(MarkerMoves markerMoves) =>
        markerMoves.Moves.Zip(markerMoves.Details, (move, detail) => new ReportedMove(move, detail));
}
