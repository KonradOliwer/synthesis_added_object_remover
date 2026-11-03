using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>The direction rule: which objects are candidates, what their surroundings are and what the surroundings decide.</summary>
public interface ISurroundingsJudging
{
    /// <returns>The target indexes, ascending, of the targets of the step's kind that the decisions left undecided.</returns>
    int[] Candidates(IReadOnlyList<TargetObject> targets, IRemovalDecisions decisions, Func<TargetObject, bool> isOfTheStepsKind);

    /// <summary>The ground area per direction around the point by what became of each box, and the state of each direction.</summary>
    /// <param name="boxesInAscendingId">In ascending id order: that fixes the order of the float sums.</param>
    SectorAreas MeasureSurroundings(IEnumerable<(OrientedBox Box, BoxFate Fate)> boxesInAscendingId, Vector3 point, int directionClearedPercent);

    /// <summary>
    /// Removed when inside another mod's object or when the surroundings are cleared, kept otherwise; a protected kind is kept
    /// instead of removed.
    /// </summary>
    LeftBehindOutcome Judge(SectorAreas surroundings, LeftBehindSurroundings rule, bool isInsideAnotherModsObject, bool isProtectedKind);
}
