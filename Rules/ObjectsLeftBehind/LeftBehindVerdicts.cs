using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>Judges an object left behind by its surroundings and joins the decision a round made into its verdict.</summary>
internal static class LeftBehindVerdicts
{
    /// <returns>The target indexes, ascending, of the targets of the step's kind that the decisions left undecided.</returns>
    public static int[] Candidates(IReadOnlyList<TargetObject> targets, IRemovalDecisions decisions, Func<TargetObject, bool> isOfTheStepsKind) =>
        [.. Enumerable.Range(0, targets.Count).Where(index => isOfTheStepsKind(targets[index]) && !decisions.IsDecided(targets[index].Id))];

    /// <summary>
    /// Removed when inside another mod's object or when the surroundings are cleared, kept otherwise; a protected kind is kept
    /// instead of removed.
    /// </summary>
    public static LeftBehindOutcome Judge(SectorAreas surroundings, LeftBehindSurroundings rule, bool isInsideAnotherModsObject, bool isProtectedKind) =>
        KeptWhenProtected(isInsideAnotherModsObject ? LeftBehindOutcome.RemovedInsideOtherObject : BySurroundings(surroundings, rule), isProtectedKind);

    /// <summary>The outcome the surroundings alone give: kept when too few directions are occupied or cleared, removed otherwise.</summary>
    public static LeftBehindOutcome BySurroundings(SectorAreas areas, LeftBehindSurroundings rule) =>
        SectorRule.Decide(areas, rule.MinOccupiedDirections, rule.ClearedDirectionsPercent) switch
        {
            SectorVerdict.TooFewOccupiedDirections => LeftBehindOutcome.KeptTooFewOccupiedDirections,
            SectorVerdict.TooFewClearedDirections => LeftBehindOutcome.KeptSurroundingsMostlyKept,
            _ when areas.RestoredCount > 0 => LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored,
            _ => LeftBehindOutcome.RemovedSurroundingsRemoved,
        };

    /// <summary>A removal proposal for each object the rules would remove.</summary>
    /// <param name="reasonOf">The step's removal reason for a removed check.</param>
    public static IReadOnlyList<ProposedRemoval> ProposalsOf<TCheck>(IEnumerable<TCheck> checks, Func<TCheck, RemovalReason> reasonOf)
        where TCheck : LeftBehindVerdict =>
    [
        .. checks
            .Where(check => check.IsRemoved)
            .Select(check => new ProposedRemoval(new TargetId(check.TargetIndex), reasonOf(check))),
    ];

    /// <summary>
    /// The check joined with what the step's round decided for its object: an object the round kept is kept as referenced, one it removed
    /// as a linked group member is removed as linked, and one it restored has the matching restored outcome.
    /// </summary>
    /// <param name="decision">The round's decision for the object; null when the round decided nothing for it.</param>
    /// <param name="decisionOf">What the round decided for a target; null for none.</param>
    public static TCheck JoinRoundDecision<TCheck>(TCheck check, Decision? decision, Func<TargetId, Decision?> decisionOf)
        where TCheck : LeftBehindVerdict
    {
        LeftBehindVerdict verdict = check;
        var effect = decision?.Effect ?? RoundEffect.Untouched;
        return (TCheck)(effect switch
        {
            { Outcome: RoundOutcome.KeptAsReferenced, KeepReason.Kind: KeepKind.CellDeleted } => verdict with { Decision = LeftBehindOutcome.KeptInDeletedCell, KeepReason = effect.KeepReason },
            { Outcome: RoundOutcome.KeptAsReferenced } => verdict with { Decision = LeftBehindOutcome.KeptReferenced, KeepReason = effect.KeepReason },
            { Outcome: RoundOutcome.Removed, AsLinked: true } => verdict with { Decision = RemovedAsLinked(decision!, decisionOf) },
            { Outcome: RoundOutcome.Untouched or RoundOutcome.Removed } => verdict,
            { Outcome: RoundOutcome.Restored, AsLinked: true } => verdict with { Decision = LeftBehindOutcome.RestoredAsLinked },
            { Outcome: RoundOutcome.Restored } => verdict with { Decision = AsRestored(verdict.Decision) },
            _ => throw new UnreachableException($"Unexpected round effect {effect} in a left-behind round."),
        });
    }

    /// <summary>Protected kinds stay whatever the rules decided; referenced objects are kept by the removal decisions.</summary>
    public static LeftBehindOutcome KeptWhenProtected(LeftBehindOutcome ruleDecision, bool isProtectedKind) =>
        LeftBehindOutcomes.IsRemoval(ruleDecision) && isProtectedKind ? LeftBehindOutcome.KeptProtectedType : ruleDecision;

    private static LeftBehindOutcome RemovedAsLinked(Decision decision, Func<TargetId, Decision?> decisionOf) =>
        decision.FollowedARestoredMember(decisionOf) ? LeftBehindOutcome.RemovedLinkedToRestoration : LeftBehindOutcome.RemovedLinkedToRemoval;

    /// <summary>The outcome of an object the rule proposed to remove and the round restored instead.</summary>
    private static LeftBehindOutcome AsRestored(LeftBehindOutcome proposed) => proposed switch
    {
        LeftBehindOutcome.RemovedInsideOtherObject => LeftBehindOutcome.RestoredInsideOtherObject,
        LeftBehindOutcome.RemovedSurroundingsRemoved => LeftBehindOutcome.RestoredSurroundingsRemoved,
        LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored => LeftBehindOutcome.RestoredSurroundingsRemovedAndRestored,
        _ => throw new UnreachableException($"The left-behind rule did not propose to remove an object with the outcome {proposed}."),
    };
}
