namespace AddedObjectRemover;

/// <summary>
/// Items grouped by space, each space indexed on first use. Items are kept in input order inside
/// their space, so a slot is the item's position among the included items of its space. Thread-safe.
/// </summary>
public sealed class ItemsBySpace<T>
{
    private readonly Dictionary<RecordKey, ItemsInSpace<T>> _spaces;
    private readonly List<ItemsInSpace<T>> _inFirstSeenOrder;
    private readonly ItemsInSpace<T> _empty;

    private ItemsBySpace(Dictionary<RecordKey, ItemsInSpace<T>> spaces, List<ItemsInSpace<T>> inFirstSeenOrder, ItemsInSpace<T> empty)
    {
        _spaces = spaces;
        _inFirstSeenOrder = inFirstSeenOrder;
        _empty = empty;
    }

    /// <param name="boxOf">The world AABB of an item, measured when its space is first queried by position.</param>
    /// <param name="isIncluded">Items it rejects are in no space.</param>
    public static ItemsBySpace<T> Create(
        IReadOnlyList<T> items,
        Func<T, RecordKey> spaceOf,
        Func<T, Box> boxOf,
        Func<T, bool> isIncluded,
        IEqualityComparer<RecordKey> spaceComparer,
        Execution execution)
    {
        var included = items.Where(isIncluded).ToList();
        var inFirstSeenOrder = KeyedGroups.IndicesByKey(included, spaceOf, spaceComparer)
            .Select(group => new ItemsInSpace<T>(group.Key, group.Indices.Select(index => included[index]).ToArray(), boxOf, execution))
            .ToList();
        var spaces = new Dictionary<RecordKey, ItemsInSpace<T>>(spaceComparer);
        foreach (var space in inFirstSeenOrder) spaces[space.Space] = space;
        return new ItemsBySpace<T>(spaces, inFirstSeenOrder, EmptySpace(boxOf, execution));
    }

    /// <summary>The spaces that hold at least one item, in the order they first appear in the input.</summary>
    public IReadOnlyList<ItemsInSpace<T>> Spaces => _inFirstSeenOrder;

    /// <returns>The items of the space; an empty set for a space without items.</returns>
    public ItemsInSpace<T> In(RecordKey space) => _spaces.GetValueOrDefault(space) ?? _empty;

    private static ItemsInSpace<T> EmptySpace(Func<T, Box> boxOf, Execution execution) =>
        new(default, [], boxOf, execution);
}
