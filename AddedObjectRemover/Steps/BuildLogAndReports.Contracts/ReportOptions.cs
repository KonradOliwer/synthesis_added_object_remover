namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <param name="Folder">Always resolved, so the log can show it even when no files are written.</param>
public sealed record ReportOptions(bool WriteFiles, string Folder);
