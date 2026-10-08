namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>Why a step proposes to remove a target object.</summary>
public abstract record RemovalReason
{
    public sealed record TooClose(OtherId Other) : RemovalReason;

    public sealed record Touching(TargetId Touched) : RemovalReason;

    /// <param name="RemovedShare">Fraction of the object's support held by removed objects.</param>
    /// <param name="MainSupporter">The removed target holding the largest share of its support.</param>
    public sealed record LostSupport(float RemovedShare, TargetId MainSupporter) : RemovalReason;

    public sealed record InsideAnotherModsObject(OtherId Container) : RemovalReason;

    public sealed record SurroundingsRemoved : RemovalReason;

    /// <param name="To">The member of the object's linked group whose removal took the group along.</param>
    public sealed record LinkedTo(TargetId To) : RemovalReason;
}
