using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover;

/// <summary>The optional report folder, whose files never change the results. Failing to access it is only a warning.</summary>
internal sealed class CsvReportOutput : IReportOutput
{
    /// <summary>Not every run writes every file, so a file an earlier run left behind would look current.</summary>
    private static readonly string[] KnownFileNames =
    [
        ReportFileNames.MeshOriginsFileName,
        ReportFileNames.SupportFileName,
        ReportFileNames.EdgesFileName,
        ReportFileNames.ComponentsFileName,
        ReportFileNames.LeftBehindFileName,
        ReportFileNames.RemovalsFileName,
        ReportFileNames.HintsFileName,
    ];

    public ReportFolderPrepared Prepare(ReportFileSettings settings)
    {
        if (!settings.WriteFiles) return new ReportFolderPrepared(StepOutcome.SwitchedOff, Warning: null);

        var warning = FileFailures.Guard(
            () => Files.DeleteListed(settings.Folder, KnownFileNames),
            exception => new ReportFileProblem(ReportFileAction.DeletingEarlierFiles, settings.Folder, FileName: null, exception.Message));
        return new ReportFolderPrepared(StepOutcome.Performed, warning);
    }

    public ReportFilesResult Write(ReportFileSettings settings, ReportTables tables)
    {
        if (tables.Outcome != StepOutcome.Performed) return new ReportFilesResult(tables.Outcome, [], [], Failure: null);

        return Failures.Attempt(
            () => WriteAll(settings, tables),
            failure => new ReportFilesResult(StepOutcome.StoppedByUnexpectedError, [], [], PartFailure.OfWholePart(ReportFileParts.ReportFiles, failure)));
    }

    private static ReportFilesResult WriteAll(ReportFileSettings settings, ReportTables tables)
    {
        var result = CsvWriter.WriteAll(
            settings.Folder,
            tables.Tables,
            (table, exception) => new ReportFileProblem(ReportFileAction.WritingFile, settings.Folder, table.FileName, exception.Message));
        return new ReportFilesResult(StepOutcome.Performed, result.Written, result.Failures, Failure: null);
    }
}
