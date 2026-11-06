using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The checks over <paramref name="targets"/>; each set of settings gets its own check, whose cell comparisons it alone reads.</summary>
internal sealed class ChangedTerrainChecks(IReadOnlyList<TargetObject> targets, TerrainPlugins plugins, ITerrainView view, IBaseObjectShapes shapes)
    : IChangedTerrainChecks
{
    public ChangedTerrainFindings? Find(ImmutableArray<TargetId> judged, ChangedTerrainSettings settings, WorkOrder order, Execution execution)
    {
        if (!settings.Runs) return null;

        var check = new ObjectTerrainCheck(view, shapes, plugins, settings);
        List<int> indexes = [.. judged.Select(target => target.Index)];
        var checks = ParallelMap.RunOver(execution, order, indexes, index => check.Check(targets[index]), ParallelMap.AutomaticRangeSize);
        return new ChangedTerrainFindings(
            [.. checks.Select(objectCheck => objectCheck.Hit).OfType<ObjectOnChangedTerrain>()],
            FailuresOf(indexes, checks),
            check.CellsCompared(),
            check.CellsComparedForWaterOnly(),
            checks.Count(objectCheck => objectCheck.WithoutTerrain));
    }

    /// <param name="indexes">The target index of each check.</param>
    private ImmutableArray<PartFailure> FailuresOf(IReadOnlyList<int> indexes, ObjectCheck[] checks) =>
    [
        .. Enumerable.Range(0, checks.Length)
            .Where(position => checks[position].Failure != null)
            .Select(position => FailureOf(targets[indexes[position]], checks[position].Failure)),
    ];

    private static PartFailure FailureOf(TargetObject target, string? failure) =>
        new(ChangedTerrainParts.TerrainCheck, failure, new FailureSubject(target.Key.ToString(), target.EditorId, target.Id.Index));
}
