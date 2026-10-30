using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>The kind of step that decided a round; a fit round comes before the removal rounds.</summary>
public enum RoundKind { Fit, ChangedTerrain, TooClose, AlsoRemove, VisibleLeftBehind, LeftBehind }

/// <param name="Number">1 for the first round of a run, counting up.</param>
public readonly record struct Round(int Number, RoundKind Kind);

/// <summary>What happened to a target object, once and for all.</summary>
/// <param name="Reason">Why a step proposed to remove it; for a fitted object, why it did not fit where it stood.</param>
public abstract record Decision(Round Round, RemovalReason Reason)
{
    /// <summary>The object is disabled in the patch, so it stands nowhere.</summary>
    public sealed record Removed(Round Round, RemovalReason Reason) : Decision(Round, Reason);

    /// <summary>The object is gone from where the cleaned mod put it and goes back to its earlier version, where it stands.</summary>
    /// <param name="EarlierVersion">The version it goes back to.</param>
    public sealed record Restored(Round Round, RemovalReason Reason, EarlierVersion EarlierVersion) : Decision(Round, Reason);

    /// <summary>A step proposed to remove the object, but it is kept.</summary>
    public sealed record Kept(Round Round, RemovalReason Reason, KeepReason KeepReason) : Decision(Round, Reason);

    /// <summary>The object did not fit where it stood and is kept there, shrunk or turned until it fits.</summary>
    /// <param name="To">Its placement now.</param>
    public sealed record Fitted(Round Round, RemovalReason Reason, EulerPlacement To) : Decision(Round, Reason);
}

/// <summary>A step's proposal to remove one target object.</summary>
public readonly record struct ProposedRemoval(TargetId Target, RemovalReason Reason);

/// <summary>A step's proposals for one round, with the kind of round they open.</summary>
public readonly record struct ProposedRound(RoundKind Kind, IReadOnlyList<ProposedRemoval> Proposals);

/// <summary>A step's proposal to keep one target object where it stands, shrunk or turned until it fits.</summary>
/// <param name="Clash">Why it would be removed as it is.</param>
/// <param name="To">The placement it fits at.</param>
public readonly record struct ProposedFit(TargetId Target, RemovalReason Clash, EulerPlacement To);
