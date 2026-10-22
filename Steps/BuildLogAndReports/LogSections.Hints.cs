using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection BuildHintsSection(CollectedObjects world, IReadOnlyList<ManualPatchHint> hints) =>
        new(
            "hints",
            hints.Count == 0
                ? []
                : [
                    $"Removed markers and kept objects: {TextFormat.Count(hints.Count)} objects.",
                    .. hints.Select(hint => HintLine(world, hint)),
                ]);

    private static string HintLine(CollectedObjects world, ManualPatchHint hint)
    {
        var target = world.Targets[hint.TargetIndex];
        return $"  {Describe.HintType(hint.Type)} {RecordNames.Describe(target)} in {Describe.Location(world, target)}: {hint.Detail}.";
    }
}
