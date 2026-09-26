using System.Numerics;

namespace AddedObjectRemover;

internal enum RelocationSurface
{
    Navmesh,
    Terrain,
}

/// <summary>A kept invisible object moved out of the other mod's object it sat inside.</summary>
internal sealed record Relocation(LeftoverEvaluation Evaluation, Vector3 From, Vector3 To, RelocationSurface Surface)
{
    public float Distance => Vector3.Distance(From, To);
}

/// <param name="LeftInPlace">Kept objects inside another mod's object for which no free spot was found.</param>
internal sealed record RelocationResult(IReadOnlyList<Relocation> Moved, IReadOnlyList<LeftoverEvaluation> LeftInPlace)
{
    public static RelocationResult None { get; } = new([], []);
}

/// <summary>
/// Moves invisible target objects that stay (protected or referenced) although they sit inside
/// another mod's visible object to the nearest spot outside every remaining visible object: on
/// the winning navmesh, else on the terrain. Only the position changes.
/// </summary>
internal sealed class KeptObjectRelocator(
    IReadOnlyList<TargetObject> targets,
    VisibleObstacles obstacles,
    NavmeshIndex navmeshes,
    TerrainSpotSearch terrain)
{
    /// <summary>Largest distance, in game units, an object is moved.</summary>
    public const float MaxMoveDistance = 2048f;

    public RelocationResult Relocate(IReadOnlyList<LeftoverEvaluation> evaluations, ParallelOptions options)
    {
        var candidates = evaluations.Where(IsKeptInsideOtherObject).ToArray();
        var relocations = new Relocation?[candidates.Length];
        Parallel.For(0, candidates.Length, options, i =>
        {
            relocations[i] = TryFindRelocation(candidates[i]);
        });
        return new RelocationResult(
            relocations.OfType<Relocation>().ToList(),
            candidates.Where((_, i) => relocations[i] == null).ToList());
    }

    private static bool IsKeptInsideOtherObject(LeftoverEvaluation evaluation) =>
        evaluation.ContainingObject != null && !evaluation.IsRemoved;

    private Relocation? TryFindRelocation(LeftoverEvaluation evaluation)
    {
        var target = targets[evaluation.TargetIndex];
        var from = target.Transform.Position;
        bool IsFree(Vector3 point) => !obstacles.IsInsideAny(target.SpaceKey, point);

        if (navmeshes.TryFindNearestFreePoint(target.SpaceKey, from, MaxMoveDistance, IsFree, out var onNavmesh))
        {
            return new Relocation(evaluation, from, onNavmesh, RelocationSurface.Navmesh);
        }
        return terrain.TryFindNearestFreePoint(target.SpaceKey, from, MaxMoveDistance, IsFree, out var onTerrain)
            ? new Relocation(evaluation, from, onTerrain, RelocationSurface.Terrain)
            : null;
    }
}
