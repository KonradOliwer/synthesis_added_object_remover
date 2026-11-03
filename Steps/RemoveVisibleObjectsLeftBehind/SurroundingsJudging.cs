using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>Hands the direction rule the visible step itself judges by to the steps that share it.</summary>
internal sealed class SurroundingsJudging : ISurroundingsJudging
{
    public int[] Candidates(IReadOnlyList<TargetObject> targets, IRemovalDecisions decisions, Func<TargetObject, bool> isOfTheStepsKind) =>
        LeftBehindVerdicts.Candidates(targets, decisions, isOfTheStepsKind);

    public SectorAreas MeasureSurroundings(IEnumerable<(OrientedBox Box, BoxFate Fate)> boxesInAscendingId, Vector3 point, int directionClearedPercent) =>
        SectorAreaTally.Measure(boxesInAscendingId, point, directionClearedPercent);

    public LeftBehindOutcome Judge(SectorAreas surroundings, LeftBehindSurroundings rule, bool isInsideAnotherModsObject, bool isProtectedKind) =>
        LeftBehindVerdicts.Judge(surroundings, rule, isInsideAnotherModsObject, isProtectedKind);
}
