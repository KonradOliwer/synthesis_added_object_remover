using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>A read-only view of the removal decisions made up to some round.</summary>
public interface IRemovalDecisions
{
    ImmutableArray<Round> Rounds { get; }

    ImmutableArray<MarkerMove> Moves { get; }

    Decision? Of(TargetId id);

    bool IsDecided(TargetId id);

    bool IsRemoved(TargetId id);

    /// <summary>Whether the target is among the objects to keep, whether or not a step has proposed it yet.</summary>
    bool IsProtected(TargetId id);

    /// <summary>The objects the round removed: its proposals in target order, then the linked group members they took along.</summary>
    ImmutableArray<TargetId> RemovedIn(Round round);

    /// <summary>The objects the round kept, in target order.</summary>
    ImmutableArray<TargetId> KeptIn(Round round);

    /// <summary>Every decided object, in target order.</summary>
    IEnumerable<(TargetId Target, Decision Decision)> All();

    /// <summary>Every removed object, in target order.</summary>
    ImmutableArray<TargetId> RemovedTargets();

    /// <summary>The rounds this view has beyond <paramref name="older"/>, an older view of the same run.</summary>
    ImmutableArray<Round> RoundsAfter(IRemovalDecisions older);
}
