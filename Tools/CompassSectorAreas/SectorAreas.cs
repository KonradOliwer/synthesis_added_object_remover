using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// Ground area of the boxes around a point and how much of it was removed and how much restored, per direction, with the
/// state each direction was judged to have.
/// </summary>
/// <param name="TotalAreas">Per direction, indexed by <see cref="DirectionSector"/>.</param>
/// <param name="RemovedAreas">Per direction, indexed by <see cref="DirectionSector"/>.</param>
/// <param name="RestoredAreas">Per direction, indexed by <see cref="DirectionSector"/>.</param>
/// <param name="States">Per direction, indexed by <see cref="DirectionSector"/>.</param>
public sealed record SectorAreas(
    ImmutableArray<float> TotalAreas,
    ImmutableArray<float> RemovedAreas,
    ImmutableArray<float> RestoredAreas,
    ImmutableArray<SectorState> States)
{
    public int OccupiedCount => States.Count(state => state != SectorState.Empty);

    /// <summary>The directions cleared, whatever cleared them: the count the surroundings are judged by.</summary>
    public int ClearedCount => States.Count(state => state is SectorState.Removed or SectorState.Restored or SectorState.RemovedAndRestored);

    /// <summary>The cleared directions with removed boxes in them.</summary>
    public int RemovedCount => States.Count(state => state is SectorState.Removed or SectorState.RemovedAndRestored);

    /// <summary>The cleared directions with restored boxes in them.</summary>
    public int RestoredCount => States.Count(state => state is SectorState.Restored or SectorState.RemovedAndRestored);

    public float Total(DirectionSector sector) => TotalAreas[(int)sector];

    public float Removed(DirectionSector sector) => RemovedAreas[(int)sector];

    public float Restored(DirectionSector sector) => RestoredAreas[(int)sector];

    public SectorState State(DirectionSector sector) => States[(int)sector];
}
