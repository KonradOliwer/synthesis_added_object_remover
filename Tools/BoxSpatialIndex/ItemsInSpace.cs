using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// The items of one space in input order; slot i is <c>Items[i]</c>. The boxes are measured and
/// indexed on the first query by position. Thread-safe.
/// </summary>
public sealed class ItemsInSpace<T>
{
    private readonly ComputedOnce<BoxIndex> _boxIndex;

    public ItemsInSpace(RecordKey space, IReadOnlyList<T> items, Func<T, Box> boxOf, Execution execution)
    {
        Space = space;
        Items = items;
        _boxIndex = new ComputedOnce<BoxIndex>(() => BoxIndex.Build(
            ParallelMap.Run(execution, items.Count, slot => boxOf(items[slot]), ParallelMap.AutomaticRangeSize)));
    }

    public RecordKey Space { get; }

    public IReadOnlyList<T> Items { get; }

    /// <summary>Items whose box is kept out of the grid for being large; measures every box if that has not happened yet.</summary>
    public int LargeItemCount => _boxIndex.Value.LargeBoxCount;

    /// <summary>Replaces <paramref name="scratch"/>'s candidates with the ascending, distinct slots of the items whose box overlaps <paramref name="area"/>.</summary>
    private List<int> FindOverlapping(Box area, SpatialQueryScratch scratch)
    {
        _boxIndex.Value.CollectOverlapping(area, scratch.Slots, scratch.Candidates);
        return scratch.Candidates;
    }

    /// <summary>Replaces <paramref name="into"/> with the ascending slots of the items whose box overlaps <paramref name="area"/> and that pass <paramref name="exact"/>.</summary>
    /// <param name="into">Not <see cref="SpatialQueryScratch.Candidates"/>, which this query fills.</param>
    /// <returns>How many items' boxes overlap the area, before the exact test.</returns>
    public int Overlapping(Box area, Func<int, bool> exact, SpatialQueryScratch scratch, List<int> into)
    {
        var candidates = FindOverlapping(area, scratch);
        into.Clear();
        foreach (var slot in candidates)
        {
            if (exact(slot)) into.Add(slot);
        }
        return candidates.Count;
    }

    /// <param name="include">Which slots count; the others are skipped.</param>
    /// <param name="exact">The exact test, run only for included slots whose box overlaps the area.</param>
    /// <returns>The lowest included slot whose box overlaps <paramref name="area"/> and that passes <paramref name="exact"/>, or -1.</returns>
    public int FirstOverlapping(Box area, Func<int, bool> include, Func<int, bool> exact, SpatialQueryScratch scratch)
    {
        foreach (var slot in FindOverlapping(area, scratch))
        {
            if (include(slot) && exact(slot)) return slot;
        }
        return -1;
    }

    /// <param name="include">Which slots count; the others are skipped.</param>
    /// <param name="contains">The exact test, run only for included slots whose box holds the point.</param>
    /// <returns>The lowest included slot that contains the point, or -1.</returns>
    public int FirstContaining(Vector3 point, Func<int, bool> include, Func<int, bool> contains, SpatialQueryScratch scratch) =>
        FirstOverlapping(new Box(point, point), include, contains, scratch);
}
