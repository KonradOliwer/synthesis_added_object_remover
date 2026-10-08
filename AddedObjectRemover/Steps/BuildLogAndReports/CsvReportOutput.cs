using System.Collections.Immutable;
using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The optional report folder, whose files never change the results. Failing to access it is only a warning.</summary>
internal sealed class CsvReportOutput : IReportOutput
{
    /// <summary>Not every run writes every file, so a file an earlier run left behind would look current.</summary>
    private static readonly string[] KnownFileNames =
    [
        ReportFileNames.MeshOriginsFileName,
        ReportFileNames.AnchoringFileName,
        ReportFileNames.EdgesFileName,
        ReportFileNames.ComponentsFileName,
        ReportFileNames.LeftBehindFileName,
        ReportFileNames.HintsFileName,
    ];

    public string? Prepare(ReportOptions options)
    {
        if (!options.WriteFiles) return null;

        return FileFailures.Guard(
            () => Files.DeleteListed(options.Folder, KnownFileNames),
            exception => Warning(options.Folder, "deleting earlier diagnostics files", exception));
    }

    public ReportFilesResult Write(ReportOptions options, ImmutableArray<CsvTable> tables)
    {
        if (!options.WriteFiles) return new ReportFilesResult([], []);

        var result = CsvWriter.WriteAll(
            options.Folder, tables, (table, exception) => Warning(options.Folder, $"writing {table.FileName}", exception));
        return new ReportFilesResult(result.Written, result.Failures);
    }

    private static string Warning(string folder, string description, Exception exception) =>
        $"  Warning: {description} in {folder} failed: {exception.Message}";
}
