using System.Collections.Immutable;

namespace AddedObjectRemover;

public static class CsvRow
{
    public static ImmutableArray<string> Of(IEnumerable<string> fields) => [.. fields];

    /// <returns>Empty fields when <paramref name="source"/> is null, otherwise its values.</returns>
    public static IEnumerable<string> EmptyOrValues<T>(T? source, int columns, Func<T, IEnumerable<string>> values) where T : class =>
        source == null ? Enumerable.Repeat(string.Empty, columns) : values(source);
}
