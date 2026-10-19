namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>
/// Among a cell's versions from the reference on (the reference at index 0, the version the game shows last), finds the version
/// whose own edit gave a shown change: the first of the last versions that all carry the shown change. When that version does not
/// count and an earlier version already carried the change, it took that version's edit back in (as a patch combining several
/// mods' changes does), and the earlier version is traced instead.
/// </summary>
/// <param name="carriesShownChange">
/// Whether the version at an index differs from the reference and matches the shown version; never true for the reference itself,
/// so the origin is always a version after it.
/// </param>
/// <param name="counts">Whether the plugin of the version at an index counts as changing the cell.</param>
internal sealed class ChangeOrigin(Func<int, bool> carriesShownChange, Func<int, bool> counts)
{
    /// <param name="shown">The index of the version the game shows, which carries the change.</param>
    public int Find(int shown)
    {
        var origin = FindStartOfCarryingRun(shown);
        while (!counts(origin) && FindLastCarrierBefore(origin) is { } earlier) origin = FindStartOfCarryingRun(earlier);
        return origin;
    }

    /// <returns>The first of the versions up to <paramref name="end"/> that all carry the shown change.</returns>
    private int FindStartOfCarryingRun(int end)
    {
        var start = end;
        while (carriesShownChange(start - 1)) start--;
        return start;
    }

    /// <returns>The last version after the reference and before <paramref name="end"/> that carries the shown change; null when none does.</returns>
    private int? FindLastCarrierBefore(int end)
    {
        for (var index = end - 1; index > 0; index--)
        {
            if (carriesShownChange(index)) return index;
        }
        return null;
    }
}
