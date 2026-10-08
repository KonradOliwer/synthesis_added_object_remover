using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    public static LogSection Hints(CollectedObjects world, IReadOnlyList<ManualPatchHint> hints) =>
        new(
            "hints",
            [
                $"Possible manual patch needed: {TextFormat.Count(hints.Count)} objects to check.",
                .. hints.Select(hint => HintLine(world, hint)),
            ]);

    private static string HintLine(CollectedObjects world, ManualPatchHint hint)
    {
        var target = world.Targets[hint.TargetIndex];
        return $"  {Describe.HintType(hint.Type)} {RecordNames.Describe(target)} in {Describe.Location(world, target)}: {hint.Detail}.";
    }
}
