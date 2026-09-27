namespace AddedObjectRemover;

/// <summary>Access to the optional diagnostics folder, whose files never change the results.</summary>
internal static class DiagnosticsFiles
{
    /// <summary>Not every run writes every file, so a file an earlier run left behind would look current.</summary>
    private static readonly string[] FileNames =
    [
        MeshOriginDiagnosticsWriter.FileName,
        AnchoringDiagnosticsWriter.FileName,
        TouchDiagnosticsWriter.EdgesFileName,
        TouchDiagnosticsWriter.ComponentsFileName,
        LeftoverDiagnosticsWriter.FileName,
        ManualPatchHintsWriter.FileName,
    ];

    /// <param name="writesReports">Stale files are left alone when report writing is off.</param>
    public static void DeleteEarlierFiles(string folder, bool writesReports)
    {
        if (!writesReports) return;

        Access(folder, "deleting earlier diagnostics files", () =>
        {
            foreach (var fileName in FileNames) CsvFile.DeleteIfPresent(Path.Combine(folder, fileName));
        });
    }

    /// <summary>Failing to access the folder is only a warning.</summary>
    public static void Access(string folder, string description, Action access)
    {
        try
        {
            access();
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            Console.WriteLine($"  Warning: {description} in {folder} failed: {ex.Message}");
        }
    }
}
