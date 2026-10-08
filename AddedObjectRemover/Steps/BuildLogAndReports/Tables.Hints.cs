using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private static readonly ImmutableArray<string> HintsHeader = ["hint", "formKey", "editorId", "space", "cell", "detail"];

    public static CsvTable Hints(CollectedObjects world, IEnumerable<ManualPatchHint> hints) =>
        new(ReportFileNames.HintsFileName, HintsHeader, [.. hints.Select(hint => HintRow(hint, world))]);

    private static ImmutableArray<string> HintRow(ManualPatchHint hint, CollectedObjects world)
    {
        var target = world.Targets[hint.TargetIndex];
        return CsvRow.Of(
        [
            Quote(hint.Type.ToString()),
            Quote(target.Key.ToString()),
            OptionalText(target.EditorId),
            Quote(Describe.Space(world, target.SpaceKey)),
            Quote(Describe.CellOrEmpty(target)),
            Quote(hint.Detail),
        ]);
    }
}
