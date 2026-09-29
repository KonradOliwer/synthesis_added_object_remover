using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <summary>Optional manual-patch-hints.csv: the "possible manual patch needed" section of the log, one row per hint.</summary>
internal static class ManualPatchHintsWriter
{
    public const string FileName = "manual-patch-hints.csv";

    private static readonly string[] Header = ["hint", "formKey", "editorId", "space", "cell", "detail"];

    /// <returns>The path written to.</returns>
    public static string Write(string folder, World world, IEnumerable<ManualPatchHint> hints)
    {
        var path = Path.Combine(folder, FileName);
        CsvFile.Write(path, Header, hints.Select(hint => FormatRow(hint, world)));
        return path;
    }

    private static IEnumerable<string> FormatRow(ManualPatchHint hint, World world)
    {
        var target = world.Targets[hint.TargetIndex];
        return
        [
            Text(hint.Type.ToString()),
            Text(target.Key.ToString()),
            Text(target.EditorId ?? string.Empty),
            Text(world.SpaceNames[target.SpaceKey]),
            Text(target.CellName ?? string.Empty),
            Text(hint.Detail),
        ];
    }
}
