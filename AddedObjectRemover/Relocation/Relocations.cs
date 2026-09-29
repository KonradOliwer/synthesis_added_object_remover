using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static class Relocations
{
    /// <returns>The kept evaluations that sit inside another mod's object, in target order.</returns>
    public static ImmutableArray<LeftoverEvaluation> Candidates(LeftoverResult leftovers, Ledger final) =>
    [
        .. leftovers.Evaluations.Where(evaluation => evaluation.ContainingObject != null && !final.IsRemoved(new TargetId(evaluation.TargetIndex))),
    ];

    public static RelocationResult Plan(RelocationInput input, RelocationOptions options)
    {
        var obstacles = new VisibleObstacles(
            input.Obstacles.Solids,
            input.Obstacles.Targets,
            Leftovers.RemovedIndexes(input.Obstacles.Final),
            input.Shapes);
        var relocator = new KeptObjectRelocator(
            input.Targets,
            index => KeptObjectRelocator.FindHomeCell(input.Targets[index], input.Handles.LocationOf(new TargetId(index))),
            [
                new NavmeshSpotSearch(new NavmeshIndex(input.Navmeshes), obstacles),
                new TerrainSpotSearch(input.Terrain, obstacles),
            ],
            options.MaxDistance);
        return relocator.Relocate(input.Candidates, input.Exec);
    }
}
