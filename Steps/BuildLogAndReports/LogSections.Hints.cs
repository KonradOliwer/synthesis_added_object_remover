using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The removed markers under one heading and the kept objects under another; a part without objects prints nothing.</summary>
    public static LogSection BuildHintsSection(CollectedObjects world, IReadOnlyList<ManualPatchHint> hints) =>
        new(
            "hints",
            [
                .. HintLines(world, [.. hints.Where(IsRemovedMarker)], RemovedMarkersHeading),
                .. HintLines(world, [.. hints.Where(hint => !IsRemovedMarker(hint))], KeptObjectsToCheckHeading),
            ]);

    private const string RemovedMarkersHeading = "Removed markers that actors or the map use";

    private const string KeptObjectsToCheckHeading = "Kept objects that would have been removed, to check by hand";

    private static bool IsRemovedMarker(ManualPatchHint hint) => hint.Type == ManualPatchHintType.RemovedMarker;

    private static IEnumerable<string> HintLines(CollectedObjects world, IReadOnlyList<ManualPatchHint> hints, string heading) =>
        hints.Count == 0 ? [] : [$"{heading}: {TextFormat.Count(hints.Count)}.", .. hints.Select(hint => HintLine(world, hint))];

    private static string HintLine(CollectedObjects world, ManualPatchHint hint)
    {
        var target = world.Targets[hint.TargetIndex];
        return $"  {Describe.HintType(hint.Type)} {RecordNames.Describe(target)} in {Describe.Location(world, target)}: {hint.Detail}.";
    }
}
