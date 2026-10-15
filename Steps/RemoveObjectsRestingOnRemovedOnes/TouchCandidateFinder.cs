using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Broad phase of the touch search: a target's oriented box grown by the tolerance, tested against
/// nearby targets of the same space (<see cref="NearPairFinder"/>). Only targets in the given spaces
/// and not excluded take part. Read-only after creation and safe to query from many threads at once.
/// </summary>
internal sealed class TouchCandidateFinder
{
    private readonly OrientedBox[] _boxes;
    private readonly NearPairFinder _pairs;

    private TouchCandidateFinder(OrientedBox[] boxes, NearPairFinder pairs)
    {
        _boxes = boxes;
        _pairs = pairs;
    }

    /// <param name="spaces">The spaces whose targets take part.</param>
    /// <param name="excluded">Targets that never take part in a pair.</param>
    public static TouchCandidateFinder Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlySet<RecordKey> spaces,
        bool[] excluded,
        IBaseObjectShapes shapes,
        float tolerance,
        Execution execution)
    {
        var measured = ParallelMap.Run(execution, targets.Count, i =>
            excluded[i] || !spaces.Contains(targets[i].SpaceKey)
                ? (OrientedBox?)null
                : OrientedBox.FromLocal(shapes.Of(targets[i].Base).Box, targets[i].Transform), ParallelMap.AutomaticRangeSize);
        var boxes = measured.Select(box => box ?? default).ToArray();
        var notTaking = measured.Select(box => box == null).ToArray();
        return new TouchCandidateFinder(boxes, NearPairFinder.Create(boxes, target => targets[target].SpaceKey, tolerance, notTaking));
    }

    /// <summary>World oriented box of an included target.</summary>
    public OrientedBox BoxOf(int target) => _boxes[target];

    /// <returns>Sorted indices of the other included targets whose boxes come within the tolerance of <paramref name="node"/>'s box; empty for a target that is not included.</returns>
    public List<int> FindNeighbors(int node) => _pairs.NeighboursOf(node);
}
