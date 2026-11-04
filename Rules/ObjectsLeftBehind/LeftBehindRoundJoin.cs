using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>The round-to-verdict rules the left-behind steps join their rounds by.</summary>
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
