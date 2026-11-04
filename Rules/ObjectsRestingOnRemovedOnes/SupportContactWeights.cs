using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// How the weights of an object's contact points are split among its supporters, and when a share of them counts as lost: the
/// support rule of target objects and other mods' objects alike (<see cref="SurfaceContacts"/>). A contact point touching
/// several supporters splits its weight equally among them (<see cref="SampleWeights.WeightHeldBy"/>).
/// </summary>
internal static class SupportContactWeights
{
    /// <summary>Shares are sums of float fractions, so support that is fully removed can add up to slightly less than 1.</summary>
    private const float ShareRoundingTolerance = 1e-5f;

    public static bool ReachesThreshold(float removedShare, float threshold) =>
        Tolerant.AtLeast(removedShare, threshold, ShareRoundingTolerance);

    public static CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, ImmutableArray<float> weights)
    {
        var supportersPerPoint = CountSupportersPerPoint(hitsBySupporter, weights.Length);
        var bySupporter = new List<SupporterWeight>();
        foreach (var (supporter, hits) in hitsBySupporter)
        {
            var weight = SampleWeights.WeightHeldBy(hits, weights, supportersPerPoint);
            if (weight > 0) bySupporter.Add(new SupporterWeight(supporter, weight));
        }

        var contactPoints = supportersPerPoint.Count(count => count > 0);
        var totalWeight = bySupporter.Sum(entry => entry.Weight);
        return new CandidateContacts(contactPoints, totalWeight, bySupporter);
    }

    public static int[] CountSupportersPerPoint(IReadOnlyList<SupporterHits> hitsBySupporter, int pointCount) =>
        SampleWeights.CountHitsPerSample(hitsBySupporter.Select(entry => entry.Hits), pointCount);
}
