using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>Judges an object left behind by its surroundings and joins the decision a round made into its verdict.</summary>
public interface ILeftBehindJudging
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

    /// <summary>A removal proposal for each check the rules would remove.</summary>
    IReadOnlyList<ProposedRemoval> ProposalsOf<TCheck>(IEnumerable<TCheck> checks, Func<TCheck, RemovalReason> reasonOf)
        where TCheck : LeftBehindVerdict;

    /// <summary>The check joined with what the step's round decided for its object.</summary>
    /// <param name="decision">The round's decision for the object; null when the round decided nothing for it.</param>
    /// <param name="decisionOf">What the round decided for a target; null for none.</param>
    TCheck JoinRoundDecision<TCheck>(TCheck check, Decision? decision, Func<TargetId, Decision?> decisionOf)
        where TCheck : LeftBehindVerdict;
}
