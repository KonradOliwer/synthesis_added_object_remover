namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>Whether the report files are written, as decided by the report writer when it prepared the folder.</summary>
/// <param name="Outcome">Switched off when no report files are written; performed otherwise.</param>
/// <param name="Warning">What failed when the earlier report files could not be deleted; null when they were, and always null when switched off.</param>
public sealed record ReportFolderPrepared(StepOutcome Outcome, ReportFileProblem? Warning);
