using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>Hands the round-to-verdict rules the visible step itself joins by to the steps that share them.</summary>
internal sealed class LeftBehindRoundJoin : ILeftBehindRoundJoin
{
    public LeftBehindOutcome KeptWhenProtected(LeftBehindOutcome ruleDecision, bool isProtectedKind) =>
        LeftBehindVerdicts.KeptWhenProtected(ruleDecision, isProtectedKind);

    public IReadOnlyList<ProposedRemoval> ProposalsOf<TCheck>(IEnumerable<TCheck> checks, Func<TCheck, RemovalReason> reasonOf)
        where TCheck : LeftBehindVerdict =>
        LeftBehindVerdicts.ProposalsOf(checks, reasonOf);

    public TCheck JoinRoundDecision<TCheck>(TCheck check, Decision? decision, Func<TargetId, Decision?> decisionOf)
        where TCheck : LeftBehindVerdict =>
        LeftBehindVerdicts.JoinRoundDecision(check, decision, decisionOf);
}
