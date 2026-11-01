using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>How another mod's object was found too close to a target object.</summary>
public abstract record TooCloseCause
{
    /// <summary>Within a multiple of the target object's own size, or an NPC stuck in the object.</summary>
    public sealed record ObjectSize : TooCloseCause;

    /// <param name="Units">The removal distance in game units the other object came within.</param>
    public sealed record Distance(float Units) : TooCloseCause;
}

/// <summary>The too-close step's reason: another mod's object came too close to the object.</summary>
public sealed record TooClose(OtherId Other, TooCloseCause Cause) : RemovalReason;
