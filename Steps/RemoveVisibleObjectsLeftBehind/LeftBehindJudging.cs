using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>Judges an object left behind by the surroundings rule and the candidate filter, and joins the round's decision into its verdict.</summary>
internal sealed class LeftBehindJudging : ILeftBehindJudging
{
    public int[] Candidates(IReadOnlyList<TargetObject> targets, IRemovalDecisions decisions, Func<TargetObject, bool> isOfTheStepsKind) =>
        LeftBehindVerdicts.Candidates(targets, decisions, isOfTheStepsKind);

    public SectorAreas MeasureSurroundings(IEnumerable<(OrientedBox Box, BoxFate Fate)> boxesInAscendingId, Vector3 point, int directionClearedPercent) =>
        SectorAreaTally.Measure(boxesInAscendingId, point, directionClearedPercent);

    public LeftBehindOutcome Judge(SectorAreas surroundings, LeftBehindSurroundings rule, bool isInsideAnotherModsObject, bool isProtectedKind) =>
        LeftBehindVerdicts.Judge(surroundings, rule, isInsideAnotherModsObject, isProtectedKind);

    public IReadOnlyList<ProposedRemoval> ProposalsOf<TCheck>(IEnumerable<TCheck> checks, Func<TCheck, RemovalReason> reasonOf)
        where TCheck : LeftBehindVerdict =>
        LeftBehindVerdicts.ProposalsOf(checks, reasonOf);

    public TCheck JoinRoundDecision<TCheck>(TCheck check, Decision? decision, Func<TargetId, Decision?> decisionOf)
        where TCheck : LeftBehindVerdict =>
        LeftBehindVerdicts.JoinRoundDecision(check, decision, decisionOf);
}
