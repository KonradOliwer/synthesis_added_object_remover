using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>The surroundings rule: whether the directions around an object are cleared.</summary>
internal static class SectorRule
{
    /// <summary>The surroundings are cleared when enough directions have objects and enough of those are cleared.</summary>
    public static SectorVerdict Decide(SectorAreas areas, int minOccupiedDirections, int clearedDirectionsPercent)
    {
        var occupied = areas.OccupiedCount;
        if (occupied < minOccupiedDirections) return SectorVerdict.TooFewOccupiedDirections;
        return SharePercent.AtLeast(areas.ClearedCount, occupied, clearedDirectionsPercent)
            ? SectorVerdict.Cleared
            : SectorVerdict.TooFewClearedDirections;
    }
}
