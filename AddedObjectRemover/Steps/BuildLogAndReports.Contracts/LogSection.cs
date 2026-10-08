using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>The lines of one part of the log, printed together.</summary>
/// <param name="Id">Names the part, for tests.</param>
public sealed record LogSection(string Id, ImmutableArray<string> Lines);
