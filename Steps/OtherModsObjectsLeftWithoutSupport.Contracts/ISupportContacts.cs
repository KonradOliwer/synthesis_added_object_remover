using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

/// <summary>
/// The also-remove step's support rule: the weighted samples of a mesh's
/// surface, which of them touch each supporter, how their weights split among the supporters, and when a lost share counts.
/// Thread-safe; results depend only on the inputs. The resting step and the other-mods step each consume a rule of the other
/// (this interface, <see cref="IAlsoRemoveRoundsAfter"/> and <see cref="ITargetObjectsDropTest"/>) without a project cycle.
/// </summary>
public interface ISupportContacts
{
    WeightedSurfaceSamples SampleSurface(string meshPath);

    /// <param name="points">Samples of the placed mesh's surface, in its local frame.</param>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    /// <returns>One per mesh supporter, in the given order, then one for the terrain when the space has terrain.</returns>
    List<SupporterHits> FindHits(
        ImmutableArray<Vector3> points, RecordKey space, PlacedTransform transform, IEnumerable<MeshSupporter> supporters, float touchDistance, List<int> scratch);

    CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, ImmutableArray<float> weights);

    bool ReachesThreshold(float lostShare, float threshold);
}
