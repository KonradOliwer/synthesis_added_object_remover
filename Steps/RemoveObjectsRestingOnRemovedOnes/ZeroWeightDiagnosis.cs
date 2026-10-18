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
        IReadOnlyList<SupportContactFinder.SupporterHits> hitsBySupporter,
        CandidateContacts contacts)
    {
        if (contacts.ContactPoints == 0 || contacts.TotalWeight > 0) return null;

        var supportersPerPoint = SupportContactFinder.CountSupportersPerPoint(hitsBySupporter, samples.Weights.Length);
        var touching = samples.Weights.Zip(supportersPerPoint).Where(sample => sample.Second > 0).Select(sample => sample.First);
        return new ZeroWeightDiagnostic(
            candidate,
            meshPath,
            contacts.ContactPoints,
            samples.Falloff,
            samples.MeshSize,
            samples.Bounds,
            samples.Weights.Length,
            CountWeights(samples.Weights),
            SumOf(samples.Weights),
            SumOf(touching),
            [.. hitsBySupporter.Select(entry => DescribeSupporter(entry, samples.Weights, supportersPerPoint))]);
    }

    private static SupporterSamples DescribeSupporter(SupportContactFinder.SupporterHits entry, float[] weights, int[] supportersPerPoint) =>
        new(entry.Supporter, entry.Hits.Count(hit => hit), SupportContactFinder.WeightHeldBy(entry.Hits, weights, supportersPerPoint));

    private static SampleWeightCounts CountWeights(float[] weights) =>
        new(
            weights.Count(weight => float.IsFinite(weight) && weight > 0),
            weights.Count(weight => float.IsFinite(weight) && !(weight > 0)),
            weights.Count(weight => !float.IsFinite(weight)));

    private static double SumOf(IEnumerable<float> weights) => weights.Sum(weight => (double)weight);
}
