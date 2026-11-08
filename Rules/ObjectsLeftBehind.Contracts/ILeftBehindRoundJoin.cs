using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>Turns the decisions a left-behind step's round made into the verdicts and removal proposals of its objects.</summary>
public interface ILeftBehindRoundJoin
{
    /// <summary>The rule's outcome, but kept instead of removed when one of <paramref name="kindsAlwaysKept"/> keeps the object's kind.</summary>
    LeftBehindOutcome KeptWhenProtected(LeftBehindOutcome ruleDecision, TargetObject target, IReadOnlyList<IKindsAlwaysKept> kindsAlwaysKept);

    /// <summary>A removal proposal for each check the rules would remove.</summary>
    IReadOnlyList<ProposedRemoval> ProposalsOf<TCheck>(IEnumerable<TCheck> checks, Func<TCheck, RemovalReason> reasonOf)
        where TCheck : LeftBehindVerdict;

    /// <summary>The check joined with what the step's round decided for its object.</summary>
    /// <param name="decision">The round's decision for the object; null when the round decided nothing for it.</param>
    /// <param name="decisionOf">What the round decided for a target; null for none.</param>
    TCheck JoinRoundDecision<TCheck>(TCheck check, Decision? decision, Func<TargetId, Decision?> decisionOf)
        where TCheck : LeftBehindVerdict;

    /// <summary>
    /// The one round of a step that judges every object once: the removal proposals of all checks decided together on a working copy of
    /// <paramref name="decisions"/>, each check joined with what the round decided, then the also-remove follow-up of what the round
    /// took from its place. Without a proposal there is no round. Nothing is judged again after the round.
    /// </summary>
    /// <param name="decisions">The decisions the checks were judged on.</param>
    /// <param name="reasonOf">The step's removal reason for a removed check.</param>
    OneRoundDecided<TCheck> DecideOneRound<TCheck>(
        IRemovalDecisions decisions, RoundOpening opening, IReadOnlyList<TCheck> checks, Func<TCheck, RemovalReason> reasonOf, IAlsoRemoveFollowUp followUp)
        where TCheck : LeftBehindVerdict;
}

/// <param name="Checks">The checks in their order, joined with the round; a check the round did not decide keeps the decision it had, such as its fit.</param>
/// <param name="ProposedRounds">The step's round and the also-remove rounds that follow it up, in the order to apply them; empty without a proposal.</param>
/// <param name="FollowUp">The also-remove follow-up of the step's round; without rounds when nothing was followed up.</param>
public sealed record OneRoundDecided<TCheck>(IReadOnlyList<TCheck> Checks, ImmutableArray<ProposedRound> ProposedRounds, RestingObjectsResult FollowUp)
    where TCheck : LeftBehindVerdict;
