using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

public enum RoundKind { TooClose, AlsoRemove, LeftBehind }

/// <param name="Number">1 for the first round of a run, counting up.</param>
public readonly record struct Round(int Number, RoundKind Kind);

/// <summary>What happened to a target object, once and for all.</summary>
public abstract record Decision(Round Round, RemovalReason Reason)
{
    public sealed record Removed(Round Round, RemovalReason Reason) : Decision(Round, Reason);

    /// <summary>A step proposed to remove the object, but it is kept.</summary>
    public sealed record Kept(Round Round, RemovalReason Reason, KeepReason KeepReason) : Decision(Round, Reason);
}

/// <summary>A step's proposal to remove one target object.</summary>
public readonly record struct ProposedRemoval(TargetId Target, RemovalReason Reason);
