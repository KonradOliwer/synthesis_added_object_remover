using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private static readonly ImmutableArray<string> LeftWithoutSupportHeader =
    [
        "outcome", "formKey", "editorId", "base", "winningMod", "worldspace", "x", "y", "z", "newZ", "supportGoneShare", "notRemovableBecause",
    ];

    /// <summary>One row per other mod's object left without support, in id order.</summary>
    /// <returns>Null when there is no row.</returns>
    public static CsvTable? BuildLeftWithoutSupportTable(CollectedObjects world, OtherModsObjectsResult settled, ReportContext context) =>
        settled.Objects.IsEmpty
            ? null
            : new CsvTable(ReportFileNames.LeftWithoutSupportFileName, LeftWithoutSupportHeader, [.. settled.Objects.Select(outcome => LeftWithoutSupportRow(world, outcome, context))]);

    /// <remarks>The new height is empty for an object that was not pulled down.</remarks>
    private static ImmutableArray<string> LeftWithoutSupportRow(CollectedObjects world, OtherModsObjectOutcome outcome, ReportContext context)
    {
        var other = outcome.Object;
        return CsvRow.Of(
        [
            Quote(OtherModsObjectText.Outcome(outcome.Fate)),
            Quote(other.Key.ToString()),
            OptionalText(other.EditorId),
            Quote(RecordNames.DescribeBase(context.Bases, other.Base)),
            Quote(other.WinningMod.ToString()),
            Quote(AreaText.Worldspace(world.Spaces[other.SpaceKey])),
            .. Numbers(other.Position),
            OptionalText(outcome.Fate == OtherModsObjectFate.PulledDown ? Number(outcome.NewPosition.Z) : null),
            Number(outcome.LostShare),
            OptionalText(OtherModsObjectText.Roles(outcome.Blocking)),
        ]);
    }
}
