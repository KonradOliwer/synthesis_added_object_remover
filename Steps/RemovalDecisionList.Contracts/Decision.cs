using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>The kind of step that decided a round; a fit round comes before the removal rounds.</summary>
public enum RoundKind { Fit, ChangedTerrain, TooClose, AlsoRemove, LeftBehind }

/// <param name="Number">1 for the first round of a run, counting up.</param>
public readonly record struct Round(int Number, RoundKind Kind);

/// <summary>Where a fitted object went.</summary>
public enum FitKind
{
    /// <summary>Kept where it stands, shrunk or turned.</summary>
    ShrunkOrTurned,

    /// <summary>A moved replacement moved back to the placement it replaces.</summary>
    MovedBack,

    /// <summary>A moved replacement moved back to the placement it replaces, and shrunk or turned there.</summary>
    MovedBackShrunkOrTurned,
}

/// <summary>What happened to a target object, once and for all.</summary>
/// <param name="Reason">Why a step proposed to remove it; for a fitted object, why it did not fit where it stood.</param>
public abstract record Decision(Round Round, RemovalReason Reason)
{
    public sealed record Removed(Round Round, RemovalReason Reason) : Decision(Round, Reason);

    /// <summary>A step proposed to remove the object, but it is kept.</summary>
    public sealed record Kept(Round Round, RemovalReason Reason, KeepReason KeepReason) : Decision(Round, Reason);

    /// <summary>The object did not fit where it stood and is kept at another placement, where it fits.</summary>
    /// <param name="To">Where it stands now.</param>
    public sealed record Fitted(Round Round, RemovalReason Reason, FitKind Kind, EulerPlacement To) : Decision(Round, Reason);
}

/// <summary>A step's proposal to remove one target object.</summary>
public readonly record struct ProposedRemoval(TargetId Target, RemovalReason Reason);

/// <summary>A step's proposal to keep one target object at another placement, where it fits.</summary>
/// <param name="Clash">Why it would be removed where it stands.</param>
/// <param name="To">The placement it fits at.</param>
public readonly record struct ProposedFit(TargetId Target, RemovalReason Clash, FitKind Kind, EulerPlacement To);
