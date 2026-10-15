using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>
/// Adds up the ground area of the visible objects around an invisible object per direction. A direction
/// without objects is empty; otherwise it is removed when its removed share of the area reaches the threshold.
/// </summary>
internal sealed class SectorAreaTally(int thresholdPercent)
{
    private readonly float[] _total = new float[CompassDirections.SectorCount];
    private readonly float[] _removed = new float[CompassDirections.SectorCount];
    private readonly int[] _objects = new int[CompassDirections.SectorCount];
    private readonly int[] _removedObjects = new int[CompassDirections.SectorCount];

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
