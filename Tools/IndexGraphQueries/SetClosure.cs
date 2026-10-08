namespace AddedObjectRemover;

public static class SetClosure
{
    /// <returns>
    /// The <paramref name="seeds"/> plus, repeatedly, every item of each group that shares an item
    /// with the result so far, until no group adds anything.
    /// </returns>
    public static HashSet<T> Of<T>(IEnumerable<T> seeds, IReadOnlyList<IReadOnlyCollection<T>> groups, IEqualityComparer<T> comparer)
    {
        var closure = new HashSet<T>(seeds, comparer);
        bool added;
        do
        {
            added = false;
            foreach (var group in groups.Where(closure.Overlaps))
            {
                foreach (var item in group) added |= closure.Add(item);
            }
        }
        while (added);
        return closure;
    }
}
