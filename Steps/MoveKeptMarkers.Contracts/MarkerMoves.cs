using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

/// <param name="Moves">Empty unless the step was performed.</param>
/// <param name="Details">What only the report needs about each of <paramref name="Moves"/>, in the same order.</param>
/// <param name="LeftInPlace">Kept markers inside another mod's object for which no free spot was found.</param>
/// <param name="MaxDistance">The largest distance, in game units, the step searched within; zero when it searched nothing.</param>
/// <param name="Failure">The step's own failure; null otherwise.</param>
public sealed record MarkerMoves(
    StepOutcome Outcome,
    ImmutableArray<MarkerMove> Moves,
    IReadOnlyList<MarkerMoveDetail> Details,
    IReadOnlyList<LeftBehindCheck> LeftInPlace,
    float MaxDistance,
    PartFailure? Failure)
{
    public static MarkerMoves SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], [], MaxDistance: 0, Failure: null);

    /// <summary>The failure is the left-behind step's and its error line already printed, so this step records none.</summary>
    public static MarkerMoves NotPerformedAfterLeftBehindStopped { get; } = new(StepOutcome.StoppedByEarlierFailure, [], [], [], MaxDistance: 0, Failure: null);
}
