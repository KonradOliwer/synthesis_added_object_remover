namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>Work counts of the too-close search: the ObjectShape zone's (zero for the BoundingBox zone) and the NPC-stuck search's.</summary>
public readonly record struct TooCloseWork(ShapeZoneWork Zone, NpcWork Npcs) : IWork<TooCloseWork>
{
    public static TooCloseWork Zero => default;

    public static TooCloseWork operator +(TooCloseWork a, TooCloseWork b) => new(a.Zone + b.Zone, a.Npcs + b.Npcs);
}
