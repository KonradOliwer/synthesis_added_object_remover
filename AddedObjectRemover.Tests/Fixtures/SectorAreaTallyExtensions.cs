namespace AddedObjectRemover.Tests;

internal static class SectorAreaTallyExtensions
{
    /// <summary>Adds a box that was removed or that stands.</summary>
    public static void Add(this SectorAreaTally tally, DirectionSector sector, float area, bool removed) =>
        tally.Add(sector, area, removed ? BoxFate.Removed : BoxFate.Stands);
}
