namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>Why a step proposes to remove a target object; each step declares its own reasons in its contracts.</summary>
public abstract record RemovalReason;

/// <summary>The decision list's own reason: the object went with its linked group.</summary>
/// <param name="To">The member of the object's linked group whose removal or restoration took the group along.</param>
public sealed record LinkedTo(TargetId To) : RemovalReason;
