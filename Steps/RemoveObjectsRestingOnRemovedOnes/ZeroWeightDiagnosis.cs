using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Records what a support evaluation worked with when a candidate has contact points but no support weight.
/// Only describes; it never changes the contacts.
/// </summary>
internal static class ZeroWeightDiagnosis
{
    /// <returns>Null unless <paramref name="contacts"/> has contact points and its total weight is not above 0.</returns>
    public static ZeroWeightDiagnostic? Of(
        int candidate,
        string meshPath,
        WeightedSurfaceSamples samples,
        IReadOnlyList<SupporterHits> hitsBySupporter,
        CandidateContacts contacts)
    {
        if (contacts.ContactPoints == 0 || contacts.TotalWeight > 0) return null;

        var supportersPerPoint = SupportContactWeights.CountSupportersPerPoint(hitsBySupporter, samples.Weights.Length);
        var touching = samples.Weights.Zip(supportersPerPoint).Where(sample => sample.Second > 0).Select(sample => sample.First);
        return new ZeroWeightDiagnostic(
            candidate,
            meshPath,
            contacts.ContactPoints,
            samples.Falloff,
            SampleWeights.FalloffOf(samples.Bounds, SurfaceContacts.OriginFalloffFraction),
            samples.Bounds,
            samples.Weights.Length,
            CountWeights(samples.Weights),
            DoubleSum.Of(samples.Weights),
            DoubleSum.Of(touching),
            [.. hitsBySupporter.Select(entry => DescribeSupporter(entry, samples.Weights, supportersPerPoint))]);
    }

    private static SupporterSamples DescribeSupporter(SupporterHits entry, float[] weights, int[] supportersPerPoint) =>
        new(entry.Supporter, entry.Hits.Count(hit => hit), SampleWeights.WeightHeldBy(entry.Hits, weights, supportersPerPoint));

    private static SampleWeightCounts CountWeights(float[] weights) =>
        new(
            weights.Count(weight => float.IsFinite(weight) && weight > 0),
            weights.Count(weight => float.IsFinite(weight) && !(weight > 0)),
            weights.Count(weight => !float.IsFinite(weight)));
}
