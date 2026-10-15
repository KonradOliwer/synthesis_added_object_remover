namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

public enum ReportFileAction
{
    DeletingEarlierFiles,
    WritingFile,
}

/// <summary>A report-folder access that failed. It is only a warning; the results never change.</summary>
/// <param name="FileName">The file that could not be written; null when the action is not about one file.</param>
public sealed record ReportFileProblem(ReportFileAction Action, string Folder, string? FileName, string ExceptionMessage);
