using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>The mesh problems first met during one phase, printed where the phase prints them.</summary>
public sealed record PhaseProblems(ImmutableArray<AssetProblem> Assets);
