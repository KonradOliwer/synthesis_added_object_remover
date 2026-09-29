using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover;

internal sealed partial record Explanations
{
    /// <summary>The touch edges after touch rounds with seeds, the mesh origins after support rounds with seeds; nothing otherwise.</summary>
    public static Explanations Compute(ExplainInput input)
    {
        var followUp = input.FollowUp;
        if (!followUp.HadSeeds) return None;

        return followUp.Mode switch
        {
            FollowUpRemovalMode.EverythingTouching => new Explanations(ExplainTouch(input), MeshOrigins: null),
            FollowUpRemovalMode.ObjectsSupportedByIt => new Explanations(
                Touch: null, MeshOriginSurvey.Measure(input.Targets, input.Shapes, input.Bases, input.Triangles, input.Exec)),
            FollowUpRemovalMode.Nothing => None,
            _ => throw new UnreachableException($"Unknown follow-up removal mode {followUp.Mode}."),
        };
    }

    private static TouchExplanation ExplainTouch(ExplainInput input)
    {
        var components = input.Components ?? throw new ArgumentException("Touch explanations need the touch components.", nameof(input));
        var search = input.FollowUp.Context!.Search;
        return new TouchExplanation(components, FindEdges(components, search, input.Exec));
    }

    /// <summary>The touching pairs with both ends, held objects included, in the same component, with their distances.</summary>
    private static ImmutableArray<TouchEdge> FindEdges(TouchComponentSet components, TouchSearch search, Execution execution)
    {
        var componentOf = components.ComponentOf;
        var nodes = Enumerable.Range(0, componentOf.Length).Where(target => componentOf[target] != TouchComponentSet.None).ToList();
        var pairs = TouchComponents.CollectCandidatePairs(search, nodes, (first, second) => componentOf[second] == componentOf[first]);
        var (results, _) = search.Tester.TestPairs(pairs, execution);
        var touching = pairs.Where((_, k) => results[k] == PairTouch.Touching).ToList();
        var distances = ParallelMap.Run(
            execution, touching.Count, () => new TouchScratch(), (i, scratch) => search.Tester.MeasureMinSurfaceDistance(touching[i], scratch));
        return [.. touching.Select((pair, i) => new TouchEdge(componentOf[pair.First], pair, distances[i]))];
    }
}
