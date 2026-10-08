using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

/// <summary>
/// Moves markers of the target that stay (protected or referenced) although they sit inside
/// another mod's visible object to the nearest spot outside every remaining visible object, trying
/// the surfaces in order (the winning navmesh, then the terrain). An exterior marker preferably
/// stays in its own cell, which the game attaches it to: only when no surface has a free spot there
/// may it move into a neighboring cell. Only the position changes. Lights, sounds, volumes, critter
/// spawners, decals, furniture and door markers are never moved: what they cover or light up, or
/// where an actor uses them, depends on their exact place.
/// </summary>
/// <param name="homeCellOf">Target index -&gt; the exterior cell it belongs to; null for an interior one, which may move anywhere.</param>
/// <param name="surfaces">The spot searches, most preferred first.</param>
/// <param name="maxDistance">Largest distance, in game units, an object is moved.</param>
internal sealed class KeptMarkerMover(
    IReadOnlyList<TargetObject> targets,
    Func<int, CellArea?> homeCellOf,
    IReadOnlyList<IFreeSpotSearch> surfaces,
    float maxDistance)
{
    private static readonly ImmutableHashSet<InvisibleObjectKind> MovableKinds =
    [
        InvisibleObjectKind.MapMarkers,
        InvisibleObjectKind.XMarkers,
        InvisibleObjectKind.IdleMarkers,
        InvisibleObjectKind.OtherMarkers,
    ];

    public MarkerMoves Move(IReadOnlyList<LeftBehindCheck> evaluations, Execution execution)
    {
        var candidates = evaluations.Where(IsMovableMarkerKeptInsideOtherObject).ToArray();
        var moves = ParallelMap.Run(
            execution,
            candidates.Length,
            () => new ObjectQueryScratch(),
            (i, scratch) => TryFindMove(candidates[i], scratch),
            rangeSize: ParallelMap.OneItemPerRange);
        return new MarkerMoves(
            ParallelResults.Compact(moves),
            ParallelResults.IndicesWhere(moves, move => move == null).Select(i => candidates[i]).ToList());
    }

    private static bool IsMovableMarkerKeptInsideOtherObject(LeftBehindCheck evaluation) =>
        evaluation.ContainingObject != null && !evaluation.IsRemoved && MovableKinds.Contains(evaluation.Kind);

    private KeptMarkerMove? TryFindMove(LeftBehindCheck evaluation, ObjectQueryScratch scratch)
    {
        if (homeCellOf(evaluation.TargetIndex) is not { } homeCell) return TryFindMoveIn(evaluation, allowedCells: null, homeCell: null, scratch);
        return TryFindMoveIn(evaluation, homeCell, homeCell, scratch)
               ?? TryFindMoveIn(evaluation, homeCell.WithNeighbors(), homeCell, scratch);
    }

    private KeptMarkerMove? TryFindMoveIn(LeftBehindCheck evaluation, CellArea? allowedCells, CellArea? homeCell, ObjectQueryScratch scratch)
    {
        var target = targets[evaluation.TargetIndex];
        var from = target.Transform.Position;
        foreach (var surface in surfaces)
        {
            if (surface.TryFindNearestFreePoint(target.SpaceKey, from, maxDistance, allowedCells, scratch, out var to))
            {
                return new KeptMarkerMove(evaluation, from, to, surface.Surface, LeftHomeCell: homeCell is { } home && !home.Contains(to));
            }
        }
        return null;
    }
}
