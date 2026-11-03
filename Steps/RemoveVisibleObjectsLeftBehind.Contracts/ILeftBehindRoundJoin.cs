using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>Turns the decisions a left-behind step's round made into the verdicts and removal proposals of its objects.</summary>
public interface ILeftBehindRoundJoin
{
    /// <summary>The rule's outcome, but kept instead of removed for a protected kind.</summary>
    LeftBehindOutcome KeptWhenProtected(LeftBehindOutcome ruleDecision, bool isProtectedKind);

    /// <summary>A removal proposal for each check the rules would remove.</summary>
    IReadOnlyList<ProposedRemoval> ProposalsOf<TCheck>(IEnumerable<TCheck> checks, Func<TCheck, RemovalReason> reasonOf)
        where TCheck : LeftBehindVerdict;

    /// <summary>The check joined with what the step's round decided for its object.</summary>
    /// <param name="decision">The round's decision for the object; null when the round decided nothing for it.</param>
    /// <param name="decisionOf">What the round decided for a target; null for none.</param>
    TCheck JoinRoundDecision<TCheck>(TCheck check, Decision? decision, Func<TargetId, Decision?> decisionOf)
        where TCheck : LeftBehindVerdict;
}
