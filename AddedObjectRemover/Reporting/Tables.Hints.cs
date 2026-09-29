using System.Collections.Immutable;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover;

internal static partial class Tables
{
    private static readonly ImmutableArray<string> HintsHeader = ["hint", "formKey", "editorId", "space", "cell", "detail"];

    public static CsvTable Hints(World world, IEnumerable<ManualPatchHint> hints) =>
        new(HintsFileName, HintsHeader, [.. hints.Select(hint => HintRow(hint, world))]);

    private static ImmutableArray<string> HintRow(ManualPatchHint hint, World world)
    {
        var target = world.Targets[hint.TargetIndex];
        return Row(
        [
            Text(hint.Type.ToString()),
            Text(target.Key.ToString()),
            Text(target.EditorId ?? string.Empty),
            Text(world.SpaceNames[target.SpaceKey]),
            Text(target.CellName ?? string.Empty),
            Text(hint.Detail),
        ]);
    }
}
