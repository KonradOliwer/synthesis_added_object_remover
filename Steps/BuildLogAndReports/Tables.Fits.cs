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
        "kind", "outcome", "formKey", "editorId", "base", "worldspace", "x", "y", "z", "rotZDeg", "scale", "newX", "newY", "newZ", "newRotZDeg", "newScale", "newBase",
    ];

    /// <summary>One row per changed object, then one per object of the target plugin kept shrunk or turned, each in target order.</summary>
    /// <returns>Null when there is no row.</returns>
    public static CsvTable? BuildFitsTable(CollectedObjects world, IRemovalDecisions final, ReportContext context)
    {
        ImmutableArray<ImmutableArray<string>> rows =
        [
            .. FitOutcomes.ChangedObjects(world).Select(changedObject => FitRow(world, final, changedObject, ChangedObjectOutcomeName(FitOutcomes.OutcomeOf(final, changedObject.Id)), context)),
            .. FitOutcomes.OwnObjectsShrunkOrTurned(world, final).Select(own => FitRow(world, final, own, OwnShrunkOrTurned, context)),
        ];
        return rows.IsEmpty ? null : new CsvTable(ReportFileNames.FitsFileName, FitsHeader, rows);
    }

    private const string OwnShrunkOrTurned = "own object shrunk or turned";

    private static string ChangedObjectOutcomeName(ChangedObjectOutcome outcome) => outcome switch
    {
        ChangedObjectOutcome.KeptWherePut => "changed object kept where moved",
        ChangedObjectOutcome.KeptShrunkOrTurned => "changed object kept shrunk or turned",
        ChangedObjectOutcome.Restored => "changed object restored",
        ChangedObjectOutcome.Removed => "changed object removed",
        _ => throw new UnreachableException($"Unknown changed object outcome {outcome}."),
    };

    /// <remarks>The new placement is empty for an object that kept its placement; for a restored object it is the earlier version's, with its base when that differs.</remarks>
    private static ImmutableArray<string> FitRow(CollectedObjects world, IRemovalDecisions final, TargetObject target, string outcome, ReportContext context)
    {
        var newPlacement = FitOutcomes.NewPlacementOf(final, target.Id);
        return CsvRow.Of(
        [
            Quote(TargetOrigins.KindName(target.Origin)),
            Quote(outcome),
            .. RecordColumns(target, context),
            Quote(AreaText.Worldspace(world.Spaces[target.SpaceKey])),
            .. PlacementColumns(target.Placement),
            .. newPlacement is { } placed ? PlacementColumns(placed) : Enumerable.Repeat(OptionalText(null), PlacementColumnCount),
            NewBaseColumn((final.Of(target.Id) as Decision.Restored)?.EarlierVersion, context),
        ]);
    }

    private const int PlacementColumnCount = 5;

    private static IEnumerable<string> PlacementColumns(EulerPlacement placement) =>
    [
        .. Numbers(placement.Position),
        Number(float.RadiansToDegrees(placement.Rotation.Z)),
        Number(placement.Scale),
    ];

    /// <summary>The earlier version's base, written like the base column; empty when there is no earlier version or its base is the winning version's.</summary>
    private static string NewBaseColumn(EarlierVersion? earlier, ReportContext context) =>
        OptionalText(earlier is { RestoresWholeRecord: true } ? RecordNames.DescribeBase(context.Bases, earlier.Base) : null);
}
