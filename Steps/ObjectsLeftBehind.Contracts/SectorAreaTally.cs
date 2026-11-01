using System.Numerics;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.ObjectsLeftBehind.Contracts;

/// <summary>
/// Adds up the ground area of the boxes around a point per direction, by what became of each box. A direction without boxes is
/// empty; otherwise it is cleared when the share of its area held by removed and restored boxes reaches the threshold.
/// </summary>
public sealed class SectorAreaTally(int thresholdPercent)
{
    private const int RemovedCategory = 0;
    private const int RestoredCategory = 1;
    private const int CategoryCount = 2;

    private readonly DirectionAreaTally _tally = new(CategoryCount);

    /// <param name="boxes">In ascending id order: that fixes the order of the float sums, so a share right at
    /// the threshold is decided the same way whatever the spatial index's layout.</param>
    public static SectorAreas Measure(IEnumerable<(OrientedBox Box, BoxFate Fate)> boxes, Vector3 point, int directionClearedPercent)
    {
        var tally = new SectorAreaTally(directionClearedPercent);
        foreach (var (box, fate) in boxes) tally.Add(SectorFootprint.Of(point, box), fate);
        return tally.Build();
    }

    public void Add(SectorFootprint footprint, BoxFate fate) => _tally.Add(footprint, CategoryOf(fate));

    public void Add(DirectionSector sector, float area, BoxFate fate) => _tally.Add(sector, area, CategoryOf(fate));

    /// <summary>
    /// The areas of a tally that holds every box of this one, the boxes in <paramref name="goneInAscendingId"/> with their fates
    /// and the others standing. This tally holds every box standing and is not changed; the result is bit for bit the one
    /// <see cref="Measure"/> gives for the same boxes.
    /// </summary>
    public SectorAreas WithGone(IEnumerable<(SectorFootprint Footprint, BoxFate Fate)> goneInAscendingId) =>
        Judge(_tally.WithCategories(goneInAscendingId.Select(gone => (gone.Footprint, CategoryOf(gone.Fate)))));

    public SectorAreas Build() => Judge(_tally.Build());

    private static int? CategoryOf(BoxFate fate) => fate switch
    {
        BoxFate.Removed => RemovedCategory,
        BoxFate.Restored => RestoredCategory,
        _ => null,
    };

    private SectorAreas Judge(DirectionAreas areas) =>
        new(
            areas.TotalAreas,
            areas.CategoryAreas[RemovedCategory],
            areas.CategoryAreas[RestoredCategory],
            [.. CompassDirections.All.Select(sector => StateOf(areas, sector))]);

    /// <summary>A direction whose objects all have no ground area is judged by object count instead.</summary>
    private SectorState StateOf(DirectionAreas areas, DirectionSector sector)
    {
        if (areas.Boxes(sector) == 0) return SectorState.Empty;
        var removedBoxes = areas.Boxes(RemovedCategory, sector);
        var restoredBoxes = areas.Boxes(RestoredCategory, sector);
        var reachesThreshold = areas.Total(sector) > 0
            ? SharePercent.AtLeast(areas.Area(RemovedCategory, sector) + areas.Area(RestoredCategory, sector), areas.Total(sector), thresholdPercent)
            : SharePercent.AtLeast(removedBoxes + restoredBoxes, areas.Boxes(sector), thresholdPercent);
        if (!reachesThreshold) return SectorState.Kept;
        if (restoredBoxes == 0) return SectorState.Removed;
        return removedBoxes == 0 ? SectorState.Restored : SectorState.RemovedAndRestored;
    }
}
