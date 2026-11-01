using System.Diagnostics;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.ObjectsLeftBehind.Contracts;

/// <summary>The rules both left-behind steps judge an object by and the way both join a round's decision into their verdicts.</summary>
public static class LeftBehindVerdicts
{
    /// <summary>The outcome the surroundings alone give: kept when too few directions are occupied or cleared, removed otherwise.</summary>
    public static LeftBehindOutcome BySurroundings(SectorAreas areas, LeftBehindSurroundings rule) =>
        SectorRule.Decide(areas, rule.MinOccupiedDirections, rule.ClearedDirectionsPercent) switch
        {
            SectorVerdict.TooFewOccupiedDirections => LeftBehindOutcome.KeptTooFewSurroundingObjects,
            SectorVerdict.TooFewClearedDirections => LeftBehindOutcome.KeptSurroundingsMostlyKept,
            _ when areas.RestoredCount > 0 => LeftBehindOutcome.RemovedSurroundingsRemovedAndRestored,
            _ => LeftBehindOutcome.RemovedSurroundingsRemoved,
        };

    /// <summary>Protected kinds stay whatever the rules decided; referenced objects are kept by the removal decisions.</summary>
    public static LeftBehindOutcome KeptWhenProtected(LeftBehindOutcome ruleDecision, bool isProtectedKind) =>
        LeftBehindOutcomes.IsRemoval(ruleDecision) && isProtectedKind ? LeftBehindOutcome.KeptProtectedType : ruleDecision;

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
            { Outcome: RoundOutcome.Restored } => verdict with { Decision = LeftBehindOutcomes.AsRestored(verdict.Decision) },
            _ => throw new UnreachableException($"Unexpected round effect {effect} in a left-behind round."),
        });
    }

    private static LeftBehindOutcome RemovedAsLinked(Decision decision, Func<TargetId, Decision?> decisionOf) =>
        decision.FollowedARestoredMember(decisionOf) ? LeftBehindOutcome.RemovedLinkedToRestoration : LeftBehindOutcome.RemovedLinkedToRemoval;
}
