using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>The direction rule the left-behind steps judge by.</summary>
internal sealed class SurroundingsJudging : ISurroundingsJudging
{
    public int[] Candidates(IReadOnlyList<TargetObject> targets, IRemovalDecisions decisions, Func<TargetObject, bool> isOfTheStepsKind) =>
        LeftBehindVerdicts.Candidates(targets, decisions, isOfTheStepsKind);

    public SectorAreas MeasureSurroundings(IEnumerable<(OrientedBox Box, BoxFate Fate)> boxesInAscendingId, Vector3 point, int directionClearedPercent) =>
        SectorAreaTally.Measure(boxesInAscendingId, point, directionClearedPercent);

    public ISurroundingsAcrossRounds MeasureAcrossRounds(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<int> candidates,
        IVisibleTargetObjects index,
        float lookAround,
        int directionClearedPercent,
        WorkOrder order,
        Execution execution) =>
        VisibleObjectSurroundings.Measure(targets, candidates, index, lookAround, directionClearedPercent, order, execution);

    public LeftBehindOutcome Judge(SectorAreas surroundings, LeftBehindSurroundings rule, bool isInsideAnotherModsObject, bool isProtectedKind) =>
        LeftBehindVerdicts.Judge(surroundings, rule, isInsideAnotherModsObject, isProtectedKind);
}
