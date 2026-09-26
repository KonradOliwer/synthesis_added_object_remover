using System.Numerics;

namespace AddedObjectRemover;

internal enum RelocationSurface
{
    Navmesh,
    Terrain,
}

/// <summary>A kept marker moved out of the other mod's object it sat inside.</summary>
internal sealed record Relocation(LeftoverEvaluation Evaluation, Vector3 From, Vector3 To, RelocationSurface Surface)
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
/// another mod's visible object to the nearest spot outside every remaining visible object: on
/// the winning navmesh, else on the terrain outside every remaining object's ground footprint.
/// Only the position changes. Lights, sounds, volumes,
/// critter spawners, decals, furniture and door markers are never moved: what they cover or light
/// up, or where an actor uses them, depends on their exact place.
/// </summary>
internal sealed class KeptObjectRelocator(
    IReadOnlyList<TargetObject> targets,
    IReadOnlyList<TargetLocation> locations,
    VisibleObstacles obstacles,
    NavmeshIndex navmeshes,
    TerrainSpotSearch terrain)
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
        Parallel.For(0, candidates.Length, options, i =>
        {
            relocations[i] = TryFindRelocation(candidates[i]);
        });
        return new RelocationResult(
            relocations.OfType<Relocation>().ToList(),
            candidates.Where((_, i) => relocations[i] == null).ToList());
    }

    private static bool IsMovableMarkerKeptInsideOtherObject(LeftoverEvaluation evaluation) =>
        evaluation.ContainingObject != null && !evaluation.IsRemoved && MovableKinds.Contains(evaluation.Kind);

    private Relocation? TryFindRelocation(LeftoverEvaluation evaluation)
    {
        var target = targets[evaluation.TargetIndex];
        var from = target.Transform.Position;
        var requiredCell = FindRequiredCell(target, locations[evaluation.TargetIndex]);
        bool IsAllowedSpot(Vector3 point) =>
            (requiredCell is not { } cell || ExteriorGrid.IsInCell(point, cell.X, cell.Y)) && !obstacles.IsInsideAny(target.SpaceKey, point);

        if (navmeshes.TryFindNearestFreePoint(target.SpaceKey, from, MaxMoveDistance, IsAllowedSpot, out var onNavmesh))
        {
            return new Relocation(evaluation, from, onNavmesh, RelocationSurface.Navmesh);
        }
        return terrain.TryFindNearestFreePoint(target.SpaceKey, from, MaxMoveDistance, requiredCell, out var onTerrain)
            ? new Relocation(evaluation, from, onTerrain, RelocationSurface.Terrain)
            : null;
    }

    /// <summary>
    /// The game attaches a temporary exterior reference to the cell that lists it, so it must stay
    /// within that cell's square. Persistent and interior references may move anywhere (null).
    /// </summary>
    private static (int X, int Y)? FindRequiredCell(TargetObject target, TargetLocation location)
    {
        var cell = location.WinningCell.Record;
        if (location.InPersistentList || cell.FormKey == target.SpaceKey || cell.Grid is not { } grid) return null;
        return (grid.Point.X, grid.Point.Y);
    }
}
