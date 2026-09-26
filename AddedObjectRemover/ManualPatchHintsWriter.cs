using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <summary>Optional manual-patch-hints.csv: the "possible manual patch needed" section of the log, one row per hint.</summary>
internal static class ManualPatchHintsWriter
{
    public const string FileName = "manual-patch-hints.csv";

    private static readonly string[] Header = ["hint", "formKey", "editorId", "space", "cell", "detail"];

    /// <returns>The path written to.</returns>
    public static string Write(string folder, ScanResult scan, IEnumerable<ManualPatchHint> hints)
    {
        var path = Path.Combine(folder, FileName);
        CsvFile.Write(path, Header, hints.Select(hint => FormatRow(hint, scan)));
        return path;
    }

    private static IEnumerable<string> FormatRow(ManualPatchHint hint, ScanResult scan)
    {
        var target = scan.Targets[hint.TargetIndex];
        return
        [
            Text(hint.Type.ToString()),
            Text(target.Record.FormKey.ToString()),
            Text(target.Record.EditorID ?? string.Empty),
            Text(scan.SpaceNames[target.SpaceKey]),
            Text(target.CellName ?? string.Empty),
            Text(hint.Detail),
        ];
    }
}
