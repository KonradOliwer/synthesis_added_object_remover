using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>
/// The also-remove rounds that follow up a step's removals pass by pass, each pass seeded by the removals of one of that step's
/// rounds; a later pass reuses the search and contacts earlier passes built.
/// </summary>
public interface IAlsoRemoveFollowUp
{
    /// <summary>
    /// Decides, on its own working copy of <paramref name="decided"/>, the also-remove rounds that follow up <paramref name="seeds"/>
    /// until a round takes nothing from its place, and proposes them. An unexpected failure ends the follow-ups: this pass and every
    /// later one propose nothing and carry the failure.
    /// </summary>
    /// <param name="decided">The decisions with every round so far, the earlier passes' rounds included as proposed.</param>
    /// <param name="seeds">Objects <paramref name="decided"/> took from their place.</param>
    FollowUpPass FollowUp(IRemovalDecisions decided, ImmutableArray<TargetId> seeds);

    /// <summary>Every pass so far as one result.</summary>
    /// <param name="final">The decisions with every pass's rounds applied as proposed.</param>
    RestingObjectsResult Summarize(IRemovalDecisions final);
}

/// <param name="ProposedRounds">Each also-remove round's proposals, in round order; empty when the pass ran no round or failed.</param>
/// <param name="Failure">The failure that ended the follow-ups, in this pass or an earlier one; null otherwise.</param>
public sealed record FollowUpPass(ImmutableArray<IReadOnlyList<ProposedRemoval>> ProposedRounds, PartFailure? Failure);
