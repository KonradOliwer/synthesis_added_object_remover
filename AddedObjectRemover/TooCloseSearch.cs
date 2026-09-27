using System.Collections.Concurrent;
using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// BoundingBox removal zone: finds visible target objects whose grown (multiplier-expanded) local
/// box contains the bounds center of some other-mod object. Other mods' placed NPCs follow the
/// <see cref="NpcClashRule"/>. Invisible targets are left to the leftover invisible objects step.
/// </summary>
internal static class TooCloseSearch
{
    /// <summary>A struct so the grid query is allocation-free and inlinable.</summary>
    private readonly struct TooCloseMatcher(OtherObjectIndex index, Box expanded, Vector3 position, Mat3 rotation, NpcClashRule npcRule)
        : IGridMatcher
    {
        public bool IsMatch(int otherIndex) =>
            !index.IsReplaced(otherIndex)
            && npcRule.TestsLikeObject(index[otherIndex])
            && index.TryGetVisibleCenter(otherIndex, out var center)
            && Geometry.IsInsideOrientedBox(center, position, rotation, expanded);
    }

    /// <summary>
    /// Each target writes only its own result slot and hits are returned in target order, so the
    /// result (including which other object is reported) does not depend on thread scheduling.
    /// </summary>
    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    public static List<TooCloseHit> FindTooCloseTargets(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        BaseObjectShapeProvider shapes,
        float multiplier,
        NpcClashRule npcRule,
        ParallelOptions parallelOptions)
    {
        var matches = new int[targets.Count];
        Parallel.ForEach(Partitioner.Create(0, targets.Count), parallelOptions, range =>
        {
            var npcScratch = new NpcScratch();
            for (var i = range.Item1; i < range.Item2; i++)
            {
                if (!visibility[i].IsVisible)
                {
                    matches[i] = -1;
                    continue;
                }
                var objectMatch = FindFirstCentreInBoxZone(targets[i], indexes[targets[i].SpaceKey], shapes, multiplier, npcRule);
                matches[i] = npcRule.ThenFirstStuckNpc(objectMatch, i, npcScratch);
            }
        });

        return ToHits(targets, indexes, matches);
    }

    /// <param name="matches">Per target, the index of the other object it is too close to, or -1.</param>
    /// <returns>The hits in target order.</returns>
    public static List<TooCloseHit> ToHits(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        IReadOnlyList<int> matches)
    {
        var hits = new List<TooCloseHit>();
        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i] >= 0) hits.Add(new TooCloseHit(i, indexes[targets[i].SpaceKey][matches[i]]));
        }
        return hits;
    }

    /// <summary>Index of the first other object whose bounds centre lies in the target's BoundingBox zone, or -1; NPCs only when <paramref name="npcRule"/> tests them like objects.</summary>
    public static int FindFirstCentreInBoxZone(
        TargetObject target, OtherObjectIndex index, BaseObjectShapeProvider shapes, float multiplier, NpcClashRule npcRule)
    {
        if (index.Count == 0) return -1;

        var position = target.Transform.Position;
        var rotation = target.Transform.Rotation;
        var expanded = Geometry.ExpandedLocalBox(shapes.GetLocalBox(target.Base), target.Transform.Scale, multiplier);
        var queryArea = Geometry.WorldAabb(expanded, position, rotation).Grown(OtherObjectIndex.RawPositionSearchMargin);

        var matcher = new TooCloseMatcher(index, expanded, position, rotation, npcRule);
        return index.Grid.TryFindFirst(queryArea, ref matcher, out var match) ? match : -1;
    }
}
