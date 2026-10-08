namespace AddedObjectRemover;

/// <summary>A CSV file that was written.</summary>
/// <param name="Elapsed">How long formatting and writing it took.</param>
public sealed record WrittenTable(string FileName, int Rows, string Path, TimeSpan Elapsed);
