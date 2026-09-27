using System.Numerics;

namespace AddedObjectRemover;

internal enum RelocationSurface
{
    Navmesh,
    Terrain,
}

/// <summary>A kept marker moved out of the other mod's object it sat inside.</summary>
/// <param name="LeftHomeCell">No free spot was found in the marker's own exterior cell, so it was moved into a neighboring one.</param>
internal sealed record Relocation(LeftoverEvaluation Evaluation, Vector3 From, Vector3 To, RelocationSurface Surface, bool LeftHomeCell)
{
    public float Distance => Vector3.Distance(From, To);
}

/// <param name="LeftInPlace">Kept markers inside another mod's object for which no free spot was found.</param>
internal sealed record RelocationResult(IReadOnlyList<Relocation> Moved, IReadOnlyList<LeftoverEvaluation> LeftInPlace)
{
    public static RelocationResult None { get; } = new([], []);
}

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
internal sealed class KeptObjectRelocator(
    IReadOnlyList<TargetObject> targets,
    Func<int, CellArea?> homeCellOf,
    IReadOnlyList<IFreeSpotSearch> surfaces)
{
    /// <summary>Largest distance, in game units, an object is moved.</summary>
    public const float MaxMoveDistance = 2048f;

    private static readonly HashSet<InvisibleObjectKind> MovableKinds =
    [
        InvisibleObjectKind.MapMarkers,
        InvisibleObjectKind.XMarkers,
        InvisibleObjectKind.IdleMarkers,
        InvisibleObjectKind.OtherMarkers,
    ];

    public RelocationResult Relocate(IReadOnlyList<LeftoverEvaluation> evaluations, ParallelOptions options)
    {
        var candidates = evaluations.Where(IsMovableMarkerKeptInsideOtherObject).ToArray();
        var relocations = new Relocation?[candidates.Length];
        Parallel.For(
            0,
            candidates.Length,
            options,
            () => new SpatialQueryScratch(),
            (i, _, scratch) =>
            {
                relocations[i] = TryFindRelocation(candidates[i], scratch);
                return scratch;
            },
            _ => { });
        return new RelocationResult(
            relocations.OfType<Relocation>().ToList(),
            candidates.Where((_, i) => relocations[i] == null).ToList());
    }

    private static bool IsMovableMarkerKeptInsideOtherObject(LeftoverEvaluation evaluation) =>
        evaluation.ContainingObject != null && !evaluation.IsRemoved && MovableKinds.Contains(evaluation.Kind);

    private Relocation? TryFindRelocation(LeftoverEvaluation evaluation, SpatialQueryScratch scratch)
    {
        if (homeCellOf(evaluation.TargetIndex) is not { } homeCell) return TryFindRelocationIn(evaluation, allowedCells: null, homeCell: null, scratch);
        return TryFindRelocationIn(evaluation, homeCell, homeCell, scratch)
               ?? TryFindRelocationIn(evaluation, homeCell.WithNeighbors(), homeCell, scratch);
    }

    private Relocation? TryFindRelocationIn(LeftoverEvaluation evaluation, CellArea? allowedCells, CellArea? homeCell, SpatialQueryScratch scratch)
    {
        var target = targets[evaluation.TargetIndex];
        var from = target.Transform.Position;
        foreach (var surface in surfaces)
        {
            if (surface.TryFindNearestFreePoint(target.SpaceKey, from, MaxMoveDistance, allowedCells, scratch, out var to))
            {
                return new Relocation(evaluation, from, to, surface.Surface, LeftHomeCell: homeCell is { } home && !home.Contains(to));
            }
        }
        return null;
    }

    /// <summary>
    /// The exterior cell whose list holds the reference, or for a reference of a worldspace's
    /// persistent cell the cell it stands in; null for an interior reference.
    /// </summary>
    public static CellArea? FindHomeCell(TargetObject target, TargetLocation location)
    {
        var cell = location.WinningCell.Record;
        if (cell.FormKey == target.SpaceKey) return null;
        if (cell.Grid is { } grid) return CellArea.Single(grid.Point.X, grid.Point.Y);
        var position = target.Transform.Position;
        return CellArea.Single(ExteriorGrid.CellIndex(position.X), ExteriorGrid.CellIndex(position.Y));
    }
}
