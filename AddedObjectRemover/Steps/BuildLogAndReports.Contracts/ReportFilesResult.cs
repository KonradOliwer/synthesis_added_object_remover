using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <param name="Written">The files written, in table order.</param>
/// <param name="Warnings">One log line per file that could not be written.</param>
public sealed record ReportFilesResult(ImmutableArray<WrittenTable> Written, ImmutableArray<string> Warnings);
