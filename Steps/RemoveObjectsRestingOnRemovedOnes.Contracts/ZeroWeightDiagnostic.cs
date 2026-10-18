namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <param name="Positive">Samples with a finite weight above 0.</param>
/// <param name="Zero">Samples with a finite weight not above 0.</param>
/// <param name="NotFinite">Samples whose weight is NaN or infinite.</param>
public readonly record struct SampleWeightCounts(int Positive, int Zero, int NotFinite);

/// <param name="TouchingSamples">Samples in contact with the supporter.</param>
/// <param name="RawWeightSum">The weight the supporter would hold, before supporters without a weight above 0 are left out.</param>
public readonly record struct SupporterSamples(Supporter Supporter, int TouchingSamples, float RawWeightSum);

/// <summary>What a candidate's support evaluation worked with when it found contact points but no support weight.</summary>
/// <param name="MeshSize">The mesh size value of the sample weights: a quarter of the bounds diagonal.</param>
/// <param name="Bounds">The mesh bounds the mesh size value comes from.</param>
/// <param name="WeightSum">The sum of all sample weights.</param>
/// <param name="TouchingWeightSum">The sum of the weights of the samples in contact with any supporter.</param>
/// <param name="Supporters">Every supporter evaluated, terrain included, in evaluation order.</param>
public sealed record ZeroWeightDiagnostic(
    int TargetIndex,
    string MeshPath,
    int ContactPoints,
    float MeshSize,
    Box Bounds,
    int Samples,
    SampleWeightCounts Weights,
    double WeightSum,
    double TouchingWeightSum,
    IReadOnlyList<SupporterSamples> Supporters);

/// <param name="EvaluatedBefore">How many candidates had their contacts found before this one in the run.</param>
public sealed record ZeroWeightCase(int EvaluatedBefore, ZeroWeightDiagnostic Diagnostic);
