using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private static readonly ImmutableArray<string> FitsHeader =
    [
        "outcome", "formKey", "editorId", "base", "worldspace", "x", "y", "z", "rotZDeg", "scale", "newX", "newY", "newZ", "newRotZDeg", "newScale",
    ];

    /// <summary>One row per moved replacement, then one per object of the target plugin kept shrunk or turned, each in target order.</summary>
    /// <returns>Null when there is no row.</returns>
    public static CsvTable? BuildFitsTable(CollectedObjects world, IRemovalDecisions final, ReportContext context)
    {
        ImmutableArray<ImmutableArray<string>> rows =
        [
            .. FitOutcomes.MovedReplacements(world).Select(replacement => FitRow(world, final, replacement, OutcomeName(FitOutcomes.OutcomeOf(final, replacement.Id)), context)),
            .. FitOutcomes.OwnObjectsShrunkOrTurned(world, final).Select(own => FitRow(world, final, own, OwnShrunkOrTurned, context)),
        ];
        return rows.IsEmpty ? null : new CsvTable(ReportFileNames.FitsFileName, FitsHeader, rows);
    }

    private const string OwnShrunkOrTurned = "own object shrunk or turned";

    private static string OutcomeName(ReplacementOutcome outcome) => outcome switch
    {
        ReplacementOutcome.KeptWhereMoved => "moved object kept where moved",
        ReplacementOutcome.KeptShrunkOrTurned => "moved object kept shrunk or turned",
        ReplacementOutcome.Removed => "moved object removed",
        _ => throw new UnreachableException($"Unknown replacement outcome {outcome}."),
    };

    /// <remarks>The new placement is empty for an object that kept its placement.</remarks>
    private static ImmutableArray<string> FitRow(CollectedObjects world, IRemovalDecisions final, TargetObject target, string outcome, ReportContext context)
    {
        var newPlacement = final.Of(target.Id) is Decision.Fitted fitted ? fitted.To : (EulerPlacement?)null;
        return CsvRow.Of(
        [
            Quote(outcome),
            .. RecordColumns(target, context),
            Quote(AreaText.Worldspace(world.Spaces[target.SpaceKey])),
            .. PlacementColumns(target.Placement),
            .. newPlacement is { } placed ? PlacementColumns(placed) : Enumerable.Repeat(OptionalText(null), PlacementColumnCount),
        ]);
    }

    private const int PlacementColumnCount = 5;

    private static IEnumerable<string> PlacementColumns(EulerPlacement placement) =>
    [
        .. Numbers(placement.Position),
        Number(float.RadiansToDegrees(placement.Rotation.Z)),
        Number(placement.Scale),
    ];
}
