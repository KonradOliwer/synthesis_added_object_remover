namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>
/// How the weights of an object's contact points are split among its supporters, and when a share of them counts as lost: the
/// support rule the also-remove step and the step for other mods' objects share. A contact point touching several supporters
/// splits its weight equally among them.
/// </summary>
public static class SupportContactWeights
{
    /// <summary>Shares are sums of float fractions, so support that is fully removed can add up to slightly less than 1.</summary>
    private const float ShareRoundingTolerance = 1e-5f;

    public static bool ReachesThreshold(float removedShare, float threshold) =>
        Tolerant.AtLeast(removedShare, threshold, ShareRoundingTolerance);

    public static CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, float[] weights)
    {
        var supportersPerPoint = CountSupportersPerPoint(hitsBySupporter, weights.Length);
        var bySupporter = new List<SupporterWeight>();
        foreach (var (supporter, hits) in hitsBySupporter)
        {
            var weight = WeightHeldBy(hits, weights, supportersPerPoint);
            if (weight > 0) bySupporter.Add(new SupporterWeight(supporter, weight));
        }

        var contactPoints = supportersPerPoint.Count(count => count > 0);
        var totalWeight = bySupporter.Sum(entry => entry.Weight);
        return new CandidateContacts(contactPoints, totalWeight, bySupporter);
    }

    public static int[] CountSupportersPerPoint(IReadOnlyList<SupporterHits> hitsBySupporter, int pointCount)
    {
        var supportersPerPoint = new int[pointCount];
        foreach (var (_, hits) in hitsBySupporter)
        {
            for (var i = 0; i < pointCount; i++)
            {
                if (hits[i]) supportersPerPoint[i]++;
            }
        }
        return supportersPerPoint;
    }

    /// <returns>The supporter's weight before supporters without a weight above 0 are left out.</returns>
    public static float WeightHeldBy(bool[] hits, float[] weights, int[] supportersPerPoint)
    {
        var weight = 0f;
        for (var i = 0; i < weights.Length; i++)
        {
            if (hits[i]) weight += weights[i] / supportersPerPoint[i];
        }
        return weight;
    }
}
