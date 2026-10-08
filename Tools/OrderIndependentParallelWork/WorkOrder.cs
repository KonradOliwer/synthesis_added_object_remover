using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// The order parallel work starts its items in, so items that share cached data run close together
/// in time. It affects speed only, never results.
/// </summary>
public sealed class WorkOrder
{
    /// <summary>Rank of each item index in <see cref="ItemsInOrder"/>.</summary>
    private readonly int[] _rank;

    /// <param name="itemsInOrder">A permutation of the item indexes.</param>
    public WorkOrder(ImmutableArray<int> itemsInOrder)
    {
        Permutation.Require(itemsInOrder, nameof(itemsInOrder));
        ItemsInOrder = itemsInOrder;
        _rank = new int[itemsInOrder.Length];
        for (var position = 0; position < itemsInOrder.Length; position++) _rank[itemsInOrder[position]] = position;
    }

    public ImmutableArray<int> ItemsInOrder { get; }

    /// <summary>Throws unless this order was built for exactly <paramref name="itemCount"/> items, so no item is skipped.</summary>
    public void RequireCovers(int itemCount)
    {
        if (ItemsInOrder.Length != itemCount)
            throw new ArgumentException($"The work order covers {ItemsInOrder.Length} items, not {itemCount}.", nameof(itemCount));
    }

    /// <param name="items">Item indexes, e.g. a step's candidates.</param>
    /// <returns>A permutation of the positions in <paramref name="items"/>, in this order; equal items keep their positions' order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An item index is not one of the items this order was built for.</exception>
    public ImmutableArray<int> Among(IReadOnlyList<int> items)
    {
        foreach (var item in items)
        {
            if ((uint)item >= (uint)_rank.Length)
                throw new ArgumentOutOfRangeException(nameof(items), item, $"The work order covers {_rank.Length} items.");
        }
        return [.. Enumerable.Range(0, items.Count).OrderBy(position => _rank[items[position]]).ThenBy(position => position)];
    }
}
