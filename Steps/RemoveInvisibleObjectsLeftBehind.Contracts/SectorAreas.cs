using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>
/// Ground area of the visible target objects around an invisible object and how much of it was
/// removed, per direction, with the state each direction was judged to have.
/// </summary>
/// <param name="TotalAreas">Per direction, indexed by <see cref="DirectionSector"/>.</param>
/// <param name="RemovedAreas">Per direction, indexed by <see cref="DirectionSector"/>.</param>
/// <param name="States">Per direction, indexed by <see cref="DirectionSector"/>.</param>
public sealed record SectorAreas(
    ImmutableArray<float> TotalAreas,
    ImmutableArray<float> RemovedAreas,
    ImmutableArray<SectorState> States)
{
    public int OccupiedCount => States.Count(state => state != SectorState.Empty);

    public int RemovedCount => States.Count(state => state == SectorState.Removed);

    public float Total(DirectionSector sector) => TotalAreas[(int)sector];

    public float Removed(DirectionSector sector) => RemovedAreas[(int)sector];

    public SectorState State(DirectionSector sector) => States[(int)sector];
}
