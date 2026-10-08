using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <param name="TouchChains">The touch chains; null unless EverythingTouching ran with seeds. When they are missing although it did, their search failed and was reported there, so no touch explanations are made.</param>
internal sealed record ReportFileDetailsInput(
    RestingObjectsRun RestingObjects,
    TouchChainSet? TouchChains,
    ImmutableArray<TargetObject> Targets,
    IBaseObjectShapes Shapes,
    ITriangleMeshes Triangles,
    IBaseFacts Bases,
    Execution Exec);

internal static class ExplanationFinder
{
    /// <summary>The touch edges after touch rounds with seeds, the mesh origins after support rounds with seeds; nothing otherwise.</summary>
    public static ReportFileDetails Compute(ReportFileDetailsInput input)
    {
        var alsoRemove = input.RestingObjects.Result;
        if (!alsoRemove.HadSeeds) return ReportFileDetails.None;

        return alsoRemove.Mode switch
        {
            FollowUpRemovalMode.EverythingTouching => input.TouchChains is { } chains
                ? new ReportFileDetails(ExplainTouch(input, chains), MeshOrigins: null)
                : ReportFileDetails.None,
            FollowUpRemovalMode.ObjectsSupportedByIt => new ReportFileDetails(
                Touch: null, MeshOriginSurvey.Measure(input.Targets, input.Shapes, input.Bases, input.Triangles, input.Exec)),
            FollowUpRemovalMode.Nothing => ReportFileDetails.None,
            _ => throw new UnreachableException($"Unknown follow-up removal mode {alsoRemove.Mode}."),
        };
    }

    private static TouchChainEdges ExplainTouch(ReportFileDetailsInput input, TouchChainSet chains)
    {
        var search = input.RestingObjects.Search!;
        return new TouchChainEdges(chains, FindEdges(chains, search, input.Exec));
    }

    /// <summary>The touching pairs with both ends, held objects included, in the same chain, with their distances.</summary>
    private static ImmutableArray<TouchEdge> FindEdges(TouchChainSet chains, TouchSearch search, Execution execution)
    {
        var componentOf = chains.ComponentOf;
        var nodes = Enumerable.Range(0, componentOf.Length).Where(target => componentOf[target] != TouchChainSet.None).ToList();
        var pairs = TouchChains.CollectCandidatePairs(search, nodes, (first, second) => componentOf[second] == componentOf[first]);
        var (results, _) = search.Tester.TestPairs(pairs, execution);
        var touching = ParallelResults.IndicesWhere(results, touch => touch == PairTouch.Touching).Select(k => pairs[k]).ToList();
        var distances = ParallelMap.Run(
            execution,
            touching.Count,
            () => new TouchScratch(),
            (i, scratch) => search.Tester.MeasureMinSurfaceDistance(touching[i], scratch),
            ParallelMap.AutomaticRangeSize);
        return [.. touching.Select((pair, i) => new TouchEdge(componentOf[pair.First], pair, distances[i]))];
    }
}
