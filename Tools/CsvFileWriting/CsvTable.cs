using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>One CSV file's content. Header and row fields are already formatted as CSV text.</summary>
public sealed record CsvTable(string FileName, ImmutableArray<string> Header, ImmutableArray<ImmutableArray<string>> Rows);
