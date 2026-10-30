using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>What a box adds to the directions around a point: its ground area, in one direction or in every direction.</summary>
/// <param name="Sector">The one direction the box counts in; null when the box contains the point and counts in every direction.</param>
public readonly record struct SectorFootprint(float GroundArea, DirectionSector? Sector)
{
    /// <summary>By sector: the list holding only that sector, so that no footprint allocates its own.</summary>
    private static readonly ImmutableArray<IReadOnlyList<DirectionSector>> SingleSectors = [.. CompassDirections.All.Select(sector => (IReadOnlyList<DirectionSector>)[sector])];

    public IReadOnlyList<DirectionSector> Sectors => Sector is { } sector ? SingleSectors[(int)sector] : CompassDirections.All;

    /// <summary>
    /// Every direction when the box contains the point. Otherwise exactly one: towards the box centre's horizontal offset when the
    /// box lies straight above or below the point (however small that offset), else towards the box's closest point.
    /// </summary>
    public static SectorFootprint Of(Vector3 point, OrientedBox box) =>
        new(box.GroundArea, box.Contains(point) ? null : CompassDirections.Towards(point, box));
}
