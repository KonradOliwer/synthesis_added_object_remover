using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>A read-only view of the removal decisions made up to some round.</summary>
public interface IRemovalDecisions
{
    ImmutableArray<Round> Rounds { get; }

    ImmutableArray<MarkerMove> Moves { get; }

    Decision? Of(TargetId id);

    bool IsDecided(TargetId id);

    /// <summary>Whether an also-remove round may still decide the object: it is undecided, or kept shrunk or turned where it stands.</summary>
    bool MayStillBeRemoved(TargetId id);

    /// <summary>Whether the object is disabled in the patch, so it stands nowhere.</summary>
    bool IsRemoved(TargetId id);

    /// <summary>
    /// Whether the object is gone from where the cleaned mod put it, disabled or restored, so it no longer holds anything up, touches
    /// or blocks anything there.
    /// </summary>
    bool HasLeftItsPlace(TargetId id);

    /// <summary>Whether the target is among the objects to keep, whether or not a step has proposed it yet.</summary>
    bool IsProtected(TargetId id);

    /// <summary>The objects the round disabled, in the order of <see cref="LeftIn"/>.</summary>
    ImmutableArray<TargetId> RemovedIn(Round round);

    /// <summary>The objects the round restored, in the order of <see cref="LeftIn"/>.</summary>
    ImmutableArray<TargetId> RestoredIn(Round round);

    /// <summary>The objects the round took from where the cleaned mod put them, disabled or restored: its proposals in target order, then the linked group members they took along.</summary>
    ImmutableArray<TargetId> LeftIn(Round round);

    /// <summary>The objects the round kept, in target order.</summary>
    ImmutableArray<TargetId> KeptIn(Round round);

    /// <summary>The objects the round kept shrunk or turned where they stand, in target order.</summary>
    ImmutableArray<TargetId> FittedIn(Round round);

    /// <summary>The objects the round kept shrunk or turned, also those a later round removed, in target order.</summary>
    ImmutableArray<TargetId> FittedAtFirstIn(Round round);

    /// <summary>Every disabled object, in target order.</summary>
    ImmutableArray<TargetId> RemovedTargets();

    /// <summary>
    /// The targets where the rounds measure them: each object kept shrunk or turned at its new placement, every other object, a
    /// restored one too, where the cleaned mod put it.
    /// </summary>
    ImmutableArray<TargetObject> TargetsAsTheRoundsMeasureThem(ImmutableArray<TargetObject> targets);

    /// <summary>Whether <see cref="TargetsAsTheRoundsMeasureThem"/> places any object other than where the cleaned mod put it.</summary>
    bool AnyMeasuredElsewhere();

    /// <summary>The targets where they stand: each object kept shrunk or turned at its new placement, each restored object at its earlier placement.</summary>
    ImmutableArray<TargetObject> TargetsAsTheyStand(ImmutableArray<TargetObject> targets);

    /// <summary>Whether <see cref="TargetsAsTheyStand"/> places any object other than where the cleaned mod put it.</summary>
    bool AnyStandsElsewhere();

    /// <summary>Every decided object, in target order.</summary>
    IEnumerable<(TargetId Target, Decision Decision)> All();

    /// <summary>The rounds this view has beyond <paramref name="older"/>, an earlier state of the same decisions: the run's earlier view, or the view a working copy started from.</summary>
    ImmutableArray<Round> RoundsAfter(IRemovalDecisions older);

    /// <summary>Decides one round of <paramref name="kind"/> from the proposals on a copy; this view and the run never see it.</summary>
    /// <returns>What the round decided, by target: the proposed objects, kept or removed, and the linked group members it removed with them.</returns>
    IReadOnlyDictionary<TargetId, Decision> DecideRoundOnCopy(RoundKind kind, IReadOnlyList<ProposedRemoval> proposals);

    /// <summary>A copy holding the decisions of this view, with a store of its own.</summary>
    IRemovalDecisionsCopy WorkingCopy();
}
