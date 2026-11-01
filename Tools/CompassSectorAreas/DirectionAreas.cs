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
}
