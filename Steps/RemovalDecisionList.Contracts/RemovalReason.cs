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
    /// <param name="ChangedBy">
    /// The plugin whose terrain or water change the game shows; null when only the patch holds it: an earlier patcher of the group
    /// made it.
    /// </param>
    public sealed record TerrainChanged(PluginName? ChangedBy, TerrainChangeKind Kind) : RemovalReason;

    public sealed record TooClose(OtherId Other, TooCloseCause Cause) : RemovalReason;

    public sealed record Touching(TargetId Touched) : RemovalReason;

    /// <param name="LeftItsPlaceShare">Fraction of the object's support held by objects that left their place, removed or restored.</param>
    /// <param name="RestoredShare">The part of <paramref name="LeftItsPlaceShare"/> held by restored objects.</param>
    /// <param name="MainSupporter">The target that left its place and holds the largest share of its support.</param>
    public sealed record LostSupport(float LeftItsPlaceShare, float RestoredShare, TargetId MainSupporter) : RemovalReason;

    /// <param name="Touched">Another mod's object left without support, removed or pulled down, that the object touched where it stood.</param>
    public sealed record TouchingAnotherModsObject(OtherId Touched) : RemovalReason;

    /// <param name="LeftItsPlaceShare">Fraction of the object's support held by target objects that left their place, removed or restored, and other mods' objects taken away.</param>
    /// <param name="RestoredShare">The part of <paramref name="LeftItsPlaceShare"/> held by restored target objects.</param>
    /// <param name="MainSupporter">The other mod's object left without support, removed or pulled down, holding the largest share of its support.</param>
    public sealed record LostSupportWithAnotherModsObject(float LeftItsPlaceShare, float RestoredShare, OtherId MainSupporter) : RemovalReason;

    public sealed record InsideAnotherModsObject(OtherId Container) : RemovalReason;

    public sealed record SurroundingsRemoved : RemovalReason;

    public sealed record VisibleObjectSurroundingsRemoved : RemovalReason;

    /// <param name="To">The member of the object's linked group whose removal or restoration took the group along.</param>
    public sealed record LinkedTo(TargetId To) : RemovalReason;
}
