using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Adds up the ground area of the boxes around a point per direction. A direction without boxes is
/// empty; otherwise it is removed when its removed share of the area reaches the threshold.
/// </summary>
public sealed class SectorAreaTally(int thresholdPercent)
{
    private readonly float[] _total = new float[CompassDirections.SectorCount];
    private readonly float[] _removed = new float[CompassDirections.SectorCount];
    private readonly int[] _objects = new int[CompassDirections.SectorCount];
    private readonly int[] _removedObjects = new int[CompassDirections.SectorCount];

    /// <param name="boxes">In ascending id order: that fixes the order of the float sums, so a share right at
    /// the threshold is decided the same way whatever the spatial index's layout.</param>
    public static SectorAreas Measure(IEnumerable<(OrientedBox Box, bool Removed)> boxes, Vector3 point, int directionClearedPercent)
    {
        var tally = new SectorAreaTally(directionClearedPercent);
        foreach (var (box, removed) in boxes)
            foreach (var sector in CompassDirections.TowardsOrAll(point, box)) tally.Add(sector, box.GroundArea, removed);
        return tally.Build();
    }

    public void Add(DirectionSector sector, float area, bool removed)
    {
        _total[(int)sector] += area;
        _objects[(int)sector]++;
        if (!removed) return;
        _removed[(int)sector] += area;
        _removedObjects[(int)sector]++;
    }

    public SectorAreas Build() =>
        new([.. _total], [.. _removed], [.. CompassDirections.All.Select(StateOf)]);

    /// <summary>A direction whose objects all have no ground area is judged by object count instead.</summary>
    private SectorState StateOf(DirectionSector sector)
    {
        var index = (int)sector;
        if (_objects[index] == 0) return SectorState.Empty;
        var reachesThreshold = _total[index] > 0
            ? SharePercent.AtLeast(_removed[index], _total[index], thresholdPercent)
            : SharePercent.AtLeast(_removedObjects[index], _objects[index], thresholdPercent);
        return reachesThreshold ? SectorState.Removed : SectorState.Kept;
    }
}
