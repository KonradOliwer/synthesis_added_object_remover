using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>The end of a run's log before the report files are written, the files' tables, and what the run decided and found.</summary>
/// <param name="Sections">The sections to print, in order.</param>
/// <param name="Tables">The tables of the report files, or why there are none.</param>
public sealed record EndOfRun(ImmutableArray<LogSection> Sections, ReportTables Tables, RunOutcome Outcome);
