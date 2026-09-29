using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    public static LogSection Hints(World world, IReadOnlyList<ManualPatchHint> hints) =>
        new(
            "hints",
            [
                $"Possible manual patch needed: {hints.Count:N0} objects to check.",
                .. hints.Select(hint => HintLine(world, hint)),
            ]);

    private static string HintLine(World world, ManualPatchHint hint)
    {
        var target = world.Targets[hint.TargetIndex];
        return $"  {Describe.HintType(hint.Type)} {RecordNames.Describe(target)} in {Describe.Location(world, target)}: {hint.Detail}.";
    }
}
