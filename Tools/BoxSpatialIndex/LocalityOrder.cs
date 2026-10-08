using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

public static class LocalityOrder
{
    /// <returns>The indexes of <paramref name="items"/> by space, then by exterior cell (X, then Y), then by index, so neighbouring items come close together.</returns>
    public static ImmutableArray<int> Of<T>(IReadOnlyList<T> items, Func<T, RecordKey> spaceOf, Func<T, Vector3> positionOf) =>
        [.. Enumerable.Range(0, items.Count)
            .OrderBy(index => spaceOf(items[index]), RecordKeyOrder.Comparer)
            .ThenBy(index => ExteriorGrid.CellIndex(positionOf(items[index]).X))
            .ThenBy(index => ExteriorGrid.CellIndex(positionOf(items[index]).Y))
            .ThenBy(index => index)];
}
