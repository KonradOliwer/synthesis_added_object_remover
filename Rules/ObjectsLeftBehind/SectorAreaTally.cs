using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>
/// Adds up the ground area of the boxes around a point per direction, by what became of each box. A direction without boxes is
/// empty; otherwise it is cleared when the share of its area held by removed and restored boxes reaches the threshold.
/// </summary>
internal sealed class SectorAreaTally(int thresholdPercent)
{
    private const int RemovedCategory = 0;
    private const int RestoredCategory = 1;
    private const int CategoryCount = 2;

    /// <summary>The categories of the boxes that left their place, in the order their areas are added up.</summary>
    private static readonly ImmutableArray<int> GoneCategories = [RemovedCategory, RestoredCategory];

    private readonly DirectionAreaTally _tally = new(CategoryCount);
    private int _objects;

    /// <param name="boxes">In ascending id order: that fixes the order of the float sums, so a share right at
    /// the threshold is decided the same way whatever the spatial index's layout.</param>
    public static SectorAreas Measure(IEnumerable<(OrientedBox Box, BoxFate Fate)> boxes, Vector3 point, int directionClearedPercent)
    {
        (SectorFootprint Footprint, int? Category)[] footprints = [.. boxes.Select(entry => (SectorFootprint.Of(point, entry.Box), CategoryOf(entry.Fate)))];
        return new SectorAreaTally(directionClearedPercent).Judge(DirectionAreaTally.Of(CategoryCount, footprints), footprints.Length);
    }

    public void Add(SectorFootprint footprint, BoxFate fate)
    {
        _tally.Add(footprint, CategoryOf(fate));
        _objects++;
    }

    /// <summary>
    /// The areas of a tally that holds every box of this one, the boxes in <paramref name="goneInAscendingId"/> with their fates
    /// and the others standing. This tally holds every box standing and is not changed; the result is bit for bit the one
    /// <see cref="Measure"/> gives for the same boxes.
    /// </summary>
    public SectorAreas WithGone(IEnumerable<(SectorFootprint Footprint, BoxFate Fate)> goneInAscendingId) =>
        Judge(_tally.WithCategories(goneInAscendingId.Select(gone => (gone.Footprint, CategoryOf(gone.Fate)))), _objects);

    public SectorAreas Build() => Judge(_tally.Build(), _objects);

    private static int? CategoryOf(BoxFate fate) => fate switch
    {
        BoxFate.Removed => RemovedCategory,
        BoxFate.Restored => RestoredCategory,
        _ => null,
    };

    private SectorAreas Judge(DirectionAreas areas, int objects) =>
        new(
            areas.TotalAreas,
            areas.CategoryAreas[RemovedCategory],
            areas.CategoryAreas[RestoredCategory],
            [.. CompassDirections.All.Select(sector => StateOf(areas, sector))],
            objects);

    private SectorState StateOf(DirectionAreas areas, DirectionSector sector)
    {
        if (areas.Boxes(sector) == 0) return SectorState.Empty;
        if (!areas.CategoriesReachShare(sector, GoneCategories, thresholdPercent)) return SectorState.Kept;
        var removedBoxes = areas.Boxes(RemovedCategory, sector);
        var restoredBoxes = areas.Boxes(RestoredCategory, sector);
        if (restoredBoxes == 0) return SectorState.Removed;
        return removedBoxes == 0 ? SectorState.Restored : SectorState.RemovedAndRestored;
    }
}
