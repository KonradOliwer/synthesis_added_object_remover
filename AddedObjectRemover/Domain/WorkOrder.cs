using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// The order parallel work visits the targets in: by space, then by exterior cell, so neighbouring
/// targets run close together in time and their meshes are still cached. It affects speed only,
/// never results.
/// </summary>
internal sealed class WorkOrder
{
    /// <summary>Rank of each target index in <see cref="TargetsBySpaceAndCell"/>.</summary>
    private readonly int[] _rank;

    /// <param name="targetsBySpaceAndCell">A permutation of the target indexes.</param>
    public WorkOrder(ImmutableArray<int> targetsBySpaceAndCell)
    {
        Permutation.Require(targetsBySpaceAndCell, nameof(targetsBySpaceAndCell));
        TargetsBySpaceAndCell = targetsBySpaceAndCell;
        _rank = new int[targetsBySpaceAndCell.Length];
        for (var position = 0; position < targetsBySpaceAndCell.Length; position++) _rank[targetsBySpaceAndCell[position]] = position;
    }

    public ImmutableArray<int> TargetsBySpaceAndCell { get; }

    public static WorkOrder Of(IReadOnlyList<TargetObject> targets) =>
        new([.. Enumerable.Range(0, targets.Count)
            .OrderBy(index => targets[index].SpaceKey, FormKeyOrder.Comparer)
            .ThenBy(index => ExteriorGrid.CellIndex(targets[index].Transform.Position.X))
            .ThenBy(index => ExteriorGrid.CellIndex(targets[index].Transform.Position.Y))
            .ThenBy(index => index)]);

    /// <summary>Throws unless this order was built for exactly <paramref name="targetCount"/> targets, so no target is skipped.</summary>
    public void RequireCovers(int targetCount)
    {
        if (TargetsBySpaceAndCell.Length != targetCount)
            throw new ArgumentException($"The work order covers {TargetsBySpaceAndCell.Length} targets, not {targetCount}.", nameof(targetCount));
    }

    /// <param name="targets">Target indexes, e.g. a step's candidates.</param>
    /// <returns>A permutation of the positions in <paramref name="targets"/>, in this order; equal targets keep their positions' order.</returns>
    public ImmutableArray<int> Among(IReadOnlyList<int> targets) =>
        [.. Enumerable.Range(0, targets.Count).OrderBy(position => _rank[targets[position]]).ThenBy(position => position)];
}
