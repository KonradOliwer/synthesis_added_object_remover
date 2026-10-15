using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <param name="Outcome">Switched off when no report files are written; stopped by an unexpected error when building the tables or writing them failed as a whole.</param>
/// <param name="Written">The files written, in table order.</param>
/// <param name="Warnings">One problem per file that could not be written, in table order.</param>
/// <param name="Failure">The failure that stopped the writing itself; null when there was none (including when the tables were not built).</param>
public sealed record ReportFilesResult(
    StepOutcome Outcome, ImmutableArray<WrittenTable> Written, ImmutableArray<ReportFileProblem> Warnings, PartFailure? Failure);
