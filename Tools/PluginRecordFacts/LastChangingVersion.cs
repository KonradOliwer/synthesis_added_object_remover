namespace AddedObjectRemover;

/// <summary>Finds the version of a record that last changed a part, whoever it is.</summary>
public static class LastChangingVersion
{
    /// <param name="versionCount">The number of versions, lowest first.</param>
    /// <param name="differsFromBelow">Whether a version's value differs from the version just below it, given the lower index first.</param>
    /// <returns>The highest index of at least 1 whose version differs from the one just below it; null when none does.</returns>
    public static int? Of(int versionCount, Func<int, int, bool> differsFromBelow)
    {
        for (var index = versionCount - 1; index >= 1; index--)
        {
            if (differsFromBelow(index - 1, index)) return index;
        }
        return null;
    }
}
