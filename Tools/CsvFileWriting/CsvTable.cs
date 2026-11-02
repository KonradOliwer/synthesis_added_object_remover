using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>One CSV file's content. Header and row fields are already formatted as CSV text.</summary>
public sealed record CsvTable(string FileName, ImmutableArray<string> Header, ImmutableArray<ImmutableArray<string>> Rows)
{
    /// <summary>The table, or null when it has no row.</summary>
    public static CsvTable? WhenAnyRow(string fileName, ImmutableArray<string> header, ImmutableArray<ImmutableArray<string>> rows) =>
        rows.IsEmpty ? null : new CsvTable(fileName, header, rows);
}
