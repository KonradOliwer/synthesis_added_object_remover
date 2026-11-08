using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>
/// The kind of step that decided a round; a fit round comes before the removal rounds. A restore round exists only on the restore
/// step's working copies, never in the run's decisions.
/// </summary>
public enum RoundKind { Fit, ChangedTerrain, TooClose, AlsoRemove, WholeGroup, VisibleLeftBehind, ClearedArea, InvisibleLeftBehind, Restore }

/// <param name="Number">1 for the first round of a run, counting up.</param>
public readonly record struct Round(int Number, RoundKind Kind);

/// <summary>How a step opens its rounds.</summary>
/// <param name="MayReplaceFits">
/// Whether the round also decides an object kept shrunk or turned where it stands, like an undecided one, which replaces its fit;
/// otherwise the round skips that object, since it fits.
/// </param>
public readonly record struct RoundOpening(RoundKind Kind, bool MayReplaceFits)
{
    /// <summary>An object kept shrunk or turned still stands where it was fitted, so the round decides it too.</summary>
    public static RoundOpening ReplacingFits(RoundKind kind) => new(kind, MayReplaceFits: true);
}

/// <summary>What happened to a target object, once and for all.</summary>
/// <param name="Reason">Why a step proposed to remove it; for a fitted object, why it did not fit where it stood.</param>
public abstract record Decision(Round Round, RemovalReason Reason)
{
    /// <summary>What the decision's round did to the object.</summary>
    public RoundEffect Effect => this switch
    {
        Removed => new RoundEffect(RoundOutcome.Removed, Reason is LinkedTo, KeepReason: null),
        Restored => new RoundEffect(RoundOutcome.Restored, Reason is LinkedTo, KeepReason: null),
        Kept kept => new RoundEffect(RoundOutcome.KeptAsReferenced, AsLinked: false, kept.KeepReason),
        Fitted => new RoundEffect(RoundOutcome.KeptShrunkOrTurned, AsLinked: false, KeepReason: null),
        _ => throw new UnreachableException($"Unknown decision {GetType().Name}."),
    };

    /// <summary>What <paramref name="round"/> did to the object; <see cref="RoundEffect.Untouched"/> when the decision is another round's.</summary>
    public RoundEffect EffectIn(Round round) => Round == round ? Effect : RoundEffect.Untouched;

    /// <summary>Whether the object was taken along as a member of a linked group by a member of its group that the same round restored.</summary>
    /// <param name="decisionOf">What was decided for a target; null for none.</param>
    public bool FollowedARestoredMember(Func<TargetId, Decision?> decisionOf) => Reason is LinkedTo linked && decisionOf(linked.To) is Restored;

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

/// <summary>A target object the restore step puts back to an earlier version, for a working copy to record.</summary>
/// <param name="To">The version it goes back to; null when that version stands nowhere (disabled or deleted), so it leaves the world.</param>
public readonly record struct GoingBack(TargetId Target, RemovalReason Reason, EarlierVersion? To);

/// <summary>A step's proposal to remove one target object.</summary>
public readonly record struct ProposedRemoval(TargetId Target, RemovalReason Reason);

/// <summary>A step's proposals for one round, with how they open it.</summary>
public readonly record struct ProposedRound(RoundOpening Opening, IReadOnlyList<ProposedRemoval> Proposals);

/// <summary>A step's proposal to keep one target object where it stands, shrunk or turned until it fits.</summary>
/// <param name="Clash">Why it would be removed as it is.</param>
/// <param name="To">The placement it fits at.</param>
public readonly record struct ProposedFit(TargetId Target, RemovalReason Clash, EulerPlacement To);
