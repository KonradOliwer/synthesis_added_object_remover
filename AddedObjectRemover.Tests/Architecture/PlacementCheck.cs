namespace AddedObjectRemover.Tests.Architecture;

internal static class PlacementCheck
{
    /// <summary>
    /// Fails unless the code is used only inside <paramref name="allowedPlaces"/>, apart from the listed exceptions.
    /// Also fails when an allowed place matches no file or an exception is no longer needed, so the lists cannot go stale.
    /// </summary>
    /// <param name="exceptions">Files outside the allowed places that may use it for now, each with the reason.</param>
    public static void AssertUsedOnlyIn(
        Func<SourceFile, bool> uses,
        IReadOnlyList<string> allowedPlaces,
        IReadOnlyDictionary<string, string>? exceptions = null)
    {
        var files = SourceFile.All();
        var missingPlaces = allowedPlaces.Where(place => !files.Any(file => file.IsIn([place]))).ToList();
        Assert.True(missingPlaces.Count == 0, $"No file at: {string.Join(", ", missingPlaces)}");

        var misplaced = files
            .Where(file => !file.IsIn(allowedPlaces) && uses(file))
            .Select(file => file.Path)
            .Order(StringComparer.Ordinal);

        Assert.Equal((exceptions?.Keys ?? Enumerable.Empty<string>()).Order(StringComparer.Ordinal), misplaced);
    }
}
