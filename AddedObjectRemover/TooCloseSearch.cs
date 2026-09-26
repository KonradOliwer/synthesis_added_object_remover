using System.Collections.Concurrent;
using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Finds target objects whose grown (multiplier-expanded) local box contains the bounds center of
/// some other-mod object.
/// </summary>
internal static class TooCloseSearch
{
    /// <summary>
    /// Margin (game units) added to a target's search AABB when querying the grid, to compensate
    /// for indexing other objects by their raw position rather than their (possibly mesh-offset)
    /// bounds center. One exterior cell width is a generous bound in practice.
    /// </summary>
    public const float OtherObjectSearchMargin = 4096f;

    /// <summary>A struct so the grid query is allocation-free and inlinable.</summary>
    private readonly struct TooCloseMatcher(OtherObjectIndex index, Box expanded, Vector3 position, Mat3 rotation)
        : IGridMatcher
    {
        public bool IsMatch(int otherIndex) =>
            !index.IsReplaced(otherIndex)
            && index.TryGetVisibleCenter(otherIndex, out var center)
            && Geometry.IsInsideOrientedBox(center, position, rotation, expanded);
    }

    /// <summary>
    /// Each target writes only its own result slot and hits are returned in target order, so the
    /// result (including which other object is reported) does not depend on thread scheduling.
    /// </summary>
    public static List<TooCloseHit> FindTooCloseTargets(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        BaseObjectShapeProvider shapes,
        float multiplier,
        ParallelOptions parallelOptions)
    {
        var matches = new int[targets.Count];
        Parallel.ForEach(Partitioner.Create(0, targets.Count), parallelOptions, range =>
        {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                matches[i] = FindFirstTooCloseOther(targets[i], indexes[targets[i].SpaceKey], shapes, multiplier);
            }
        });

        var hits = new List<TooCloseHit>();
        for (var i = 0; i < matches.Length; i++)
        {
            if (matches[i] >= 0) hits.Add(new TooCloseHit(i, indexes[targets[i].SpaceKey][matches[i]]));
        }
        return hits;
    }

    /// <summary>Index of the first too-close other object, or -1.</summary>
    private static int FindFirstTooCloseOther(TargetObject target, OtherObjectIndex index, BaseObjectShapeProvider shapes, float multiplier)
    {
        if (index.Count == 0) return -1;

        var position = target.Transform.Position;
        var rotation = target.Transform.Rotation;
        var expanded = Geometry.ExpandedLocalBox(shapes.GetLocalBox(target.Base), target.Transform.Scale, multiplier);
        var searchArea = Geometry.WorldAabb(expanded, position, rotation);
        var margin = new Vector3(OtherObjectSearchMargin);
        var queryArea = new Box(searchArea.Min - margin, searchArea.Max + margin);

        var matcher = new TooCloseMatcher(index, expanded, position, rotation);
        return index.Grid.TryFindFirst(queryArea, ref matcher, out var match) ? match : -1;
    }
}
