using System.Diagnostics;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>What a round did to one object.</summary>
public enum RoundOutcome
{
    /// <summary>There is no decision for the object, or it is another round's.</summary>
    Untouched,

    /// <summary>The round disabled it.</summary>
    Removed,

    /// <summary>The round took it from where the cleaned mod put it and restored it to its earlier version.</summary>
    Restored,

    /// <summary>The round proposed to remove it, but something refers to it, so it is kept.</summary>
    KeptAsReferenced,

    /// <summary>The round kept it where it stands, shrunk or turned until it fits.</summary>
    KeptShrunkOrTurned,
}

/// <param name="AsLinked">The round removed or restored the object as a member of a linked group, not for its own reason; false for any other outcome.</param>
/// <param name="KeepReason">Why a referenced object is kept; null for any other outcome.</param>
public readonly record struct RoundEffect(RoundOutcome Outcome, bool AsLinked, KeepReason? KeepReason);

/// <summary>The one reader of what a round did to an object.</summary>
public static class RoundEffects
{
    public static RoundEffect Of(Decision? decision) => decision switch
    {
        Decision.Removed removed => new RoundEffect(RoundOutcome.Removed, removed.Reason is RemovalReason.LinkedTo, null),
        Decision.Restored restored => new RoundEffect(RoundOutcome.Restored, restored.Reason is RemovalReason.LinkedTo, null),
        Decision.Kept kept => new RoundEffect(RoundOutcome.KeptAsReferenced, false, kept.KeepReason),
        Decision.Fitted => new RoundEffect(RoundOutcome.KeptShrunkOrTurned, false, null),
        null => new RoundEffect(RoundOutcome.Untouched, false, null),
        _ => throw new UnreachableException($"Unknown decision {decision}."),
    };

    /// <summary>Whether the object was taken along as a member of a linked group by a member of its group that the same round restored.</summary>
    /// <param name="decisionOf">What was decided for a target; null for none.</param>
    public static bool FollowedARestoredMember(Decision? decision, Func<TargetId, Decision?> decisionOf) =>
        decision is { Reason: RemovalReason.LinkedTo linked } && decisionOf(linked.To) is Decision.Restored;

    /// <summary>What <paramref name="round"/> did to the object; <see cref="RoundOutcome.Untouched"/> when the decision is another round's.</summary>
    public static RoundEffect Of(Decision? decision, Round round) => Of(decision?.Round == round ? decision : null);
}
