using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <param name="Iteration">The follow-up round that evaluated it, from 1.</param>
/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
/// <param name="Removed">Removed for its own lost support.</param>
/// <param name="RemovedAsLinked">Not removed for its own support, but in the same round with a linked object that left its place, removed or restored.</param>
/// <param name="RemovedAsLinkedToRestored">The part of <paramref name="RemovedAsLinked"/> where the linked object was restored; false when <paramref name="RemovedAsLinked"/> is false.</param>
/// <param name="Restored">Taken from where the cleaned mod put it for its own lost support and put back to its earlier version.</param>
/// <param name="RestoredAsLinked">Not restored for its own support, but in the same round with a linked object that left its place.</param>
/// <param name="Held">It would have lost enough support, but it is protected.</param>
public sealed record SupportEvaluationRow(
    int TargetIndex,
    int Iteration,
    CandidateContacts Contacts,
    IReadOnlyList<SupporterShare> Shares,
    bool Removed,
    bool RemovedAsLinked,
    bool RemovedAsLinkedToRestored,
    bool Restored,
    bool RestoredAsLinked,
    bool Held);
