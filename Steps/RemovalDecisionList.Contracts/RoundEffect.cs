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

/// <summary>What a round did to one object; each <see cref="Decision"/> gives its own.</summary>
/// <param name="AsLinked">The round removed or restored the object as a member of a linked group, not for its own reason; false for any other outcome.</param>
/// <param name="KeepReason">Why a referenced object is kept; null for any other outcome.</param>
public readonly record struct RoundEffect(RoundOutcome Outcome, bool AsLinked, KeepReason? KeepReason)
{
    /// <summary>What a round did to an object it decided nothing for.</summary>
    public static readonly RoundEffect Untouched = new(RoundOutcome.Untouched, AsLinked: false, KeepReason: null);
}
