using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>The other-mod object a target is too close to, and how it was found.</summary>
internal readonly record struct ZoneHit(OtherId Other, TooCloseCause Cause)
{
    /// <summary>How the rule words the finding as the reason of a removal.</summary>
    public TooClose ToReason() => new(Other, Cause);
}
