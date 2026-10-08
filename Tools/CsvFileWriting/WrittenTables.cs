using System.Collections.Immutable;

namespace AddedObjectRemover;

public static class WrittenTables
{
    /// <returns>Null when no table with that file name was written.</returns>
    public static WrittenTable? Find(ImmutableArray<WrittenTable> tables, string fileName) =>
        tables.FirstOrDefault(table => table.FileName == fileName);
}
