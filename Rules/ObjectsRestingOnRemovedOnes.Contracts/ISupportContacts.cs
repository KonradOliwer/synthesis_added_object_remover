using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>
/// The also-remove support rule: the weighted samples of a mesh's
/// surface, which of them touch each supporter, how their weights split among the supporters, and when a lost share counts.
/// Thread-safe; results depend only on the inputs.
/// </summary>
public interface ISupportContacts
{
    WeightedSurfaceSamples SampleSurface(string meshPath);

    /// <param name="points">Samples of the placed mesh's surface, in its local frame.</param>
    /// <param name="terrain">The ground of the world the mesh is judged in.</param>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    /// <returns>One per mesh supporter, in the given order, then one for the terrain when the space has terrain.</returns>
    List<SupporterHits> FindHits(
        ImmutableArray<Vector3> points, RecordKey space, PlacedTransform transform, IEnumerable<MeshSupporter> supporters, ITerrainHeights terrain, float touchDistance, List<int> scratch);

    CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, ImmutableArray<float> weights);

    bool ReachesThreshold(float lostShare, float threshold);
}
