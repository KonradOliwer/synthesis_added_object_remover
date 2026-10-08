using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <param name="Iteration">The follow-up round that evaluated it, from 1.</param>
/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
/// <param name="Removed">Removed for its own lost support.</param>
/// <param name="RemovedAsLinked">Not removed for its own support, but in the same round with a linked object that was.</param>
/// <param name="Held">It would have lost enough support, but it is protected.</param>
public sealed record AnchoringEvaluation(
    int TargetIndex,
    int Iteration,
    CandidateContacts Contacts,
    IReadOnlyList<SupporterShare> Shares,
    bool Removed,
    bool RemovedAsLinked,
    bool Held)
{
    public float RemovedShare => ShareOf(SupportCategory.RemovedTarget);

    public float ShareOf(SupportCategory category) => SupporterShares.TotalOf(Shares, category);
}
