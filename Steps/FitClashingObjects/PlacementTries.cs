using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.FitClashingObjects;

/// <summary>
/// The placements a clashing object is tried at, in order; the first that fits wins: the object shrunk or turned where it stands.
/// The adjustments go from the largest size down, each size from the smallest turn up, a turn the other way after the turn the
/// one way: an object stays as big as it can, then as near its own heading as it can. Shrink limit 100 and turn limit 0 give none.
/// </summary>
internal sealed class PlacementTries(FitSettings settings)
{
    /// <summary>The size goes down in steps of this many percentage points, the shrink limit itself last.</summary>
    private const int ScaleStepPercent = 5;

    /// <summary>The heading turns in steps of this many degrees, the turn limit itself last.</summary>
    private const int TurnStepDegrees = 15;

    /// <summary>Turning this far either way reaches every heading.</summary>
    private const int HalfTurnDegrees = 180;

    private readonly ImmutableArray<(int Percent, int Degrees)> _adjustments = ListAdjustments(settings);

    /// <param name="adjustable">Whether the object may be shrunk or turned.</param>
    /// <returns>Empty when the object is not tried.</returns>
    public ImmutableArray<EulerPlacement> For(TargetObject target, bool adjustable) => adjustable ? Adjusted(target.Placement) : [];

    private ImmutableArray<EulerPlacement> Adjusted(EulerPlacement placement) =>
    [
        .. _adjustments.Select(adjustment => placement with
        {
            Rotation = placement.Rotation with { Z = placement.Rotation.Z + float.DegreesToRadians(adjustment.Degrees) },
            Scale = placement.Scale * adjustment.Percent / Percent.PerWhole,
        }),
    ];

    /// <summary>Every size with every turn, in the order they are tried, without the object as it is.</summary>
    private static ImmutableArray<(int Percent, int Degrees)> ListAdjustments(FitSettings settings) =>
    [
        .. SizesInPercent(settings.ShrinkLimitPercent)
            .SelectMany(percent => TurnsInDegrees(settings.TurnLimitDegrees), (percent, degrees) => (Percent: percent, Degrees: degrees))
            .Where(adjustment => adjustment != (Percent.PerWhole, 0)),
    ];

    private static IEnumerable<int> SizesInPercent(int shrinkLimitPercent)
    {
        for (var percent = Percent.PerWhole; percent > shrinkLimitPercent; percent -= ScaleStepPercent) yield return percent;
        yield return Math.Min(shrinkLimitPercent, Percent.PerWhole);
    }

    private static IEnumerable<int> TurnsInDegrees(int turnLimitDegrees)
    {
        var limit = Math.Min(turnLimitDegrees, HalfTurnDegrees);
        yield return 0;
        for (var degrees = TurnStepDegrees; degrees < limit; degrees += TurnStepDegrees)
        {
            yield return degrees;
            yield return -degrees;
        }
        if (limit <= 0) yield break;
        yield return limit;
        if (limit < HalfTurnDegrees) yield return -limit;
    }
}
