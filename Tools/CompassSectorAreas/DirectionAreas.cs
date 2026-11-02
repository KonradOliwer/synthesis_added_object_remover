using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Per direction around a point, the ground area and the number of boxes: in all and in each category the caller numbered.</summary>
/// <param name="TotalAreas">Per direction, indexed by <see cref="DirectionSector"/>.</param>
/// <param name="TotalBoxes">Per direction, indexed by <see cref="DirectionSector"/>.</param>
/// <param name="CategoryAreas">By category, then per direction.</param>
/// <param name="CategoryBoxes">By category, then per direction.</param>
public sealed record DirectionAreas(
    ImmutableArray<float> TotalAreas,
    ImmutableArray<int> TotalBoxes,
    ImmutableArray<ImmutableArray<float>> CategoryAreas,
    ImmutableArray<ImmutableArray<int>> CategoryBoxes)
{
    public float Total(DirectionSector sector) => TotalAreas[(int)sector];

    public int Boxes(DirectionSector sector) => TotalBoxes[(int)sector];

    public float Area(int category, DirectionSector sector) => CategoryAreas[category][(int)sector];

    public int Boxes(int category, DirectionSector sector) => CategoryBoxes[category][(int)sector];

    /// <summary>
    /// Whether the boxes in the given categories hold at least <paramref name="percent"/> of the direction's ground area. A direction
    /// whose boxes all have no ground area is judged by the number of its boxes instead.
    /// </summary>
    /// <param name="categories">Their areas are added up from zero in this order.</param>
    public bool CategoriesReachShare(DirectionSector sector, ImmutableArray<int> categories, int percent) =>
        Total(sector) > 0
            ? SharePercent.AtLeast(AreaOf(categories, sector), Total(sector), percent)
            : SharePercent.AtLeast(categories.Sum(category => Boxes(category, sector)), Boxes(sector), percent);

    private float AreaOf(ImmutableArray<int> categories, DirectionSector sector)
    {
        var area = 0f;
        foreach (var category in categories) area += Area(category, sector);
        return area;
    }
}
