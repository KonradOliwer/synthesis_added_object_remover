using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>How each target object looks in the game: visible, or which kind of invisible object it is.</summary>
internal static class Looks
{
    /// <summary>Measures every target base once, up front, so the classification only reads the results.</summary>
    /// <returns>Parallel to <paramref name="targets"/>.</returns>
    public static TargetLooks OfTargets(IReadOnlyList<TargetObject> targets, ShapeCatalog shapes, Execution execution)
    {
        shapes.BuildArchiveIndexNow();
        shapes.MeasureBases(BasesOf(targets), execution);
        return new([.. ParallelMap.Run(execution, targets.Count, index =>
        {
            var target = targets[index];
            return shapes.GetVisibility(target.Base, target.IsPrimitive, target.HasMapMarker);
        })]);
    }

    /// <summary>The distinct bases of the target objects, objects without a base left out.</summary>
    public static ImmutableArray<BaseRef> BasesOf(IEnumerable<TargetObject> targets) =>
        [.. targets.Select(target => target.Base).OfType<BaseRef>().DistinctBy(baseRef => baseRef.FormKey)];
}
