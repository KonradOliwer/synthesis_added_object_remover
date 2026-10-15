using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

/// <summary>Finds the nearest spot on one kind of surface that is clear of every remaining visible object. Thread-safe.</summary>
internal interface IFreeSpotSearch
{
    /// <summary>Distance, in game units, a spot keeps from every remaining visible object, so it is not on the edge of one.</summary>
    const float Clearance = 16f;

    RelocationSurface Surface { get; }

    /// <param name="allowedCells">
    /// The exterior cells the spot must lie in, at least <see cref="Clearance"/> from their outer
    /// border; null when it may lie anywhere.
    /// </param>
    /// <param name="scratch">The calling thread's buffers.</param>
    bool TryFindNearestFreePoint(RecordKey spaceKey, Vector3 point, float maxDistance, CellArea? allowedCells, ObjectQueryScratch scratch, out Vector3 found);
}
