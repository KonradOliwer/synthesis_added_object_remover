namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>What changed in the terrain under or near a target object; a removal can name several.</summary>
[Flags]
public enum TerrainChangeKind
{
    None = 0,
    Height = 1,
    TerrainType = 2,
    WaterHeight = 4,
    WaterAdded = 8,
    WaterRemoved = 16,
}

/// <summary>How another mod's object was found too close to a target object.</summary>
public abstract record TooCloseCause
{
    /// <summary>Within a multiple of the target object's own size, or an NPC stuck in the object.</summary>
    public sealed record ObjectSize : TooCloseCause;

    /// <param name="Units">The removal distance in game units the other object came within.</param>
    public sealed record Distance(float Units) : TooCloseCause;
}

/// <summary>Why a step proposes to remove a target object.</summary>
public abstract record RemovalReason
{
    /// <param name="ChangedBy">The plugin whose terrain or water change the game shows.</param>
    public sealed record TerrainChanged(PluginName ChangedBy, TerrainChangeKind Kind) : RemovalReason;

    public sealed record TooClose(OtherId Other, TooCloseCause Cause) : RemovalReason;

    public sealed record Touching(TargetId Touched) : RemovalReason;

    /// <param name="RemovedShare">Fraction of the object's support held by removed objects.</param>
    /// <param name="MainSupporter">The removed target holding the largest share of its support.</param>
    public sealed record LostSupport(float RemovedShare, TargetId MainSupporter) : RemovalReason;

    public sealed record InsideAnotherModsObject(OtherId Container) : RemovalReason;

    public sealed record SurroundingsRemoved : RemovalReason;

    /// <param name="To">The member of the object's linked group whose removal took the group along.</param>
    public sealed record LinkedTo(TargetId To) : RemovalReason;
}
