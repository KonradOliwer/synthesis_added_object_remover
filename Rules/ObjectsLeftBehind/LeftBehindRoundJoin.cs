using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>The round-to-verdict rules the left-behind steps join their rounds by.</summary>
internal sealed class LeftBehindRoundJoin : ILeftBehindRoundJoin
{
    public LeftBehindOutcome KeptWhenProtected(LeftBehindOutcome ruleDecision, TargetObject target, IReadOnlyList<IKindsAlwaysKept> kindsAlwaysKept) =>
        LeftBehindVerdicts.KeptWhenProtected(ruleDecision, isProtectedKind: kindsAlwaysKept.Any(kinds => kinds.Keeps(target)));

    public IReadOnlyList<ProposedRemoval> ProposalsOf<TCheck>(IEnumerable<TCheck> checks, Func<TCheck, RemovalReason> reasonOf)
        where TCheck : LeftBehindVerdict =>
        LeftBehindVerdicts.ProposalsOf(checks, reasonOf);

    public TCheck JoinRoundDecision<TCheck>(TCheck check, Decision? decision, Func<TargetId, Decision?> decisionOf)
        where TCheck : LeftBehindVerdict =>
        LeftBehindVerdicts.JoinRoundDecision(check, decision, decisionOf);

    public OneRoundDecided<TCheck> DecideOneRound<TCheck>(
        IRemovalDecisions decisions, RoundOpening opening, IReadOnlyList<TCheck> checks, Func<TCheck, RemovalReason> reasonOf, IAlsoRemoveFollowUp followUp)
        where TCheck : LeftBehindVerdict =>
        OneLeftBehindRound.Decide(decisions, opening, checks, reasonOf, followUp);
}
