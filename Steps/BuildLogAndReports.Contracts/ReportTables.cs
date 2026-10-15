using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <param name="Outcome">Switched off when no report files are written; stopped by an unexpected error when building the tables failed.</param>
/// <param name="Tables">Empty unless the outcome is performed.</param>
/// <param name="Failure">The failure that stopped the building; null otherwise.</param>
public sealed record ReportTables(StepOutcome Outcome, ImmutableArray<CsvTable> Tables, PartFailure? Failure)
{
    public static ReportTables SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], Failure: null);
}
