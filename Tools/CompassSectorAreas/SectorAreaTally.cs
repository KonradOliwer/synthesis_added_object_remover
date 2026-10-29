using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>What became of a box around a point: it stands, it was removed, or it was restored (it left the place the point is judged in).</summary>
public enum BoxFate
{
    Stands,
    Removed,
    Restored,
}

/// <summary>
/// Adds up the ground area of the boxes around a point per direction. A direction without boxes is
/// empty; otherwise it is cleared when the share of its area held by removed and restored boxes reaches the threshold.
/// </summary>
public sealed class SectorAreaTally(int thresholdPercent)
{
    private readonly float[] _total = new float[CompassDirections.SectorCount];
    private readonly float[] _removed = new float[CompassDirections.SectorCount];
    private readonly float[] _restored = new float[CompassDirections.SectorCount];
    private readonly int[] _objects = new int[CompassDirections.SectorCount];
    private readonly int[] _removedObjects = new int[CompassDirections.SectorCount];
    private readonly int[] _restoredObjects = new int[CompassDirections.SectorCount];

    /// <param name="boxes">In ascending id order: that fixes the order of the float sums, so a share right at
    /// the threshold is decided the same way whatever the spatial index's layout.</param>
    public static SectorAreas Measure(IEnumerable<(OrientedBox Box, BoxFate Fate)> boxes, Vector3 point, int directionClearedPercent)
    {
        var tally = new SectorAreaTally(directionClearedPercent);
        foreach (var (box, fate) in boxes)
            foreach (var sector in CompassDirections.TowardsOrAll(point, box)) tally.Add(sector, box.GroundArea, fate);
        return tally.Build();
    }

    public void Add(DirectionSector sector, float area, BoxFate fate)
    {
        var index = (int)sector;
        _total[index] += area;
        _objects[index]++;
        switch (fate)
        {
            case BoxFate.Removed:
                _removed[index] += area;
                _removedObjects[index]++;
                break;
            case BoxFate.Restored:
                _restored[index] += area;
                _restoredObjects[index]++;
                break;
        }
    }

    public SectorAreas Build() =>
        new([.. _total], [.. _removed], [.. _restored], [.. CompassDirections.All.Select(StateOf)]);

    /// <summary>A direction whose objects all have no ground area is judged by object count instead.</summary>
    private SectorState StateOf(DirectionSector sector)
    {
        var index = (int)sector;
        if (_objects[index] == 0) return SectorState.Empty;
        var reachesThreshold = _total[index] > 0
            ? SharePercent.AtLeast(_removed[index] + _restored[index], _total[index], thresholdPercent)
            : SharePercent.AtLeast(_removedObjects[index] + _restoredObjects[index], _objects[index], thresholdPercent);
        if (!reachesThreshold) return SectorState.Kept;
        if (_restoredObjects[index] == 0) return SectorState.Removed;
        return _removedObjects[index] == 0 ? SectorState.Restored : SectorState.RemovedAndRestored;
    }
}
