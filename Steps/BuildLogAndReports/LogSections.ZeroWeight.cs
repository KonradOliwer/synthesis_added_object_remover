using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    private const int CoordinateDecimals = 1;

    /// <returns>One line per candidate with contact points but no support weight, round by round, each in target order.</returns>
    private static string[] ZeroWeightLines(CollectedObjects world, RestingObjectsResult alsoRemove) =>
    [
        .. alsoRemove.Evidence.OfType<SupportRoundDetails>().SelectMany(
            (details, index) => details.ZeroWeights.Select(zeroWeight => ZeroWeightLine(world, zeroWeight, iteration: index + 1))),
    ];

    private static string ZeroWeightLine(CollectedObjects world, ZeroWeightCase zeroWeight, int iteration)
    {
        var (evaluatedBefore, diagnostic) = (zeroWeight.EvaluatedBefore, zeroWeight.Diagnostic);
        var target = world.Targets[diagnostic.TargetIndex];
        var weights = diagnostic.Weights;
        return $"  Anchoring found no support weight for {RecordNames.Describe(target)} in {Describe.Space(world, target.SpaceKey)} "
            + $"(mesh {diagnostic.MeshPath}, iteration {TextFormat.Count(iteration)}, {TextFormat.Count(evaluatedBefore)} candidates evaluated before it in this run): "
            + $"{TextFormat.Count(diagnostic.ContactPoints)} contact points; mesh size value {Exact(diagnostic.MeshSize)} "
            + $"from bounds {Corner(diagnostic.Bounds.Min)} to {Corner(diagnostic.Bounds.Max)}; "
            + $"{TextFormat.Count(diagnostic.Samples)} samples: {TextFormat.Count(weights.Positive)} with weight above 0, {TextFormat.Count(weights.Zero)} at 0, "
            + $"{TextFormat.Count(weights.NotFinite)} not finite; weights add up to {Exact(diagnostic.WeightSum)} "
            + $"({Exact(diagnostic.TouchingWeightSum)} for the touching samples); "
            + $"supporters: {string.Join(Describe.ListSeparator, diagnostic.Supporters.Select(supporter => DescribeSupporterSamples(world, supporter)))}.";
    }

    private static string DescribeSupporterSamples(CollectedObjects world, SupporterSamples supporter) =>
        $"{Tables.DescribeSupporter(supporter.Supporter, world)} {TextFormat.Count(supporter.TouchingSamples)} touching samples, raw weight {Exact(supporter.RawWeightSum)}";

    private static string Corner(Vector3 corner) =>
        $"({TextFormat.Fixed(corner.X, CoordinateDecimals)}, {TextFormat.Fixed(corner.Y, CoordinateDecimals)}, {TextFormat.Fixed(corner.Z, CoordinateDecimals)})";

    private static string Exact<T>(T value) where T : IFormattable => value.ToString("R", CultureInfo.InvariantCulture);
}
