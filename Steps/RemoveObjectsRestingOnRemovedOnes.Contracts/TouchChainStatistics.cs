namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <param name="ComponentsWithFollowUpRemovals">Components with at least one object removed for touching a removed one.</param>
/// <param name="LargestComponent">The most removed objects in one component.</param>
/// <param name="Levels">Also-remove rounds that reached at least one object.</param>
/// <param name="MaxDepth">The longest chain, in steps, from a seed to an object reached from it.</param>
/// <param name="Pairs">The also-remove rounds' pair tests; the chains' own tests are not counted.</param>
public sealed record TouchChainStatistics(
    int Components,
    int ComponentsWithFollowUpRemovals,
    int LargestComponent,
    int Levels,
    int MaxDepth,
    PairTestStats Pairs);

/// <summary>A touching pair of targets in the same component; First &lt; Second.</summary>
public readonly record struct TouchEdge(int ComponentId, IndexPair Pair, float MinSurfaceDistance);
