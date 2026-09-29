using System.Collections.Concurrent;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// BoundingBox removal zone: finds visible target objects whose grown (multiplier-expanded) local
/// box contains the bounds center of some other-mod object. Other mods' placed NPCs follow the
/// <see cref="NpcClashRule"/>. Invisible targets are left to the leftover invisible objects step.
/// </summary>
internal static class TooCloseSearch
{
    /// <summary>
    /// Each target writes only its own result slot and hits are returned in target order, so the
    /// result (including which other object is reported) does not depend on thread scheduling.
    /// </summary>
    /// <param name="visibility">Parallel to <paramref name="targets"/>.</param>
    public static List<TooCloseHit> FindTooCloseTargets(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements,
        BaseObjectShapeProvider shapes,
        float multiplier,
        NpcClashRule npcRule,
        ParallelOptions parallelOptions)
    {
        var matches = new int[targets.Count];
        Parallel.ForEach(Partitioner.Create(0, targets.Count), parallelOptions, range =>
        {
            var npcScratch = new NpcScratch();
            var slots = new List<int>();
            var candidates = new List<int>();
            for (var i = range.Item1; i < range.Item2; i++)
            {
                if (!visibility[i].IsVisible)
                {
                    matches[i] = -1;
                    continue;
                }
                var objectMatch = FindFirstCentreInBoxZone(
                    targets[i], indexes[targets[i].SpaceKey], replacements, shapes, multiplier, npcRule, slots, candidates);
                matches[i] = npcRule.ThenFirstStuckNpc(objectMatch, i, npcScratch);
            }
            npcRule.AddStats(npcScratch);
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

    /// <summary>
    /// Lowest index of an other object whose bounds centre lies in the target's BoundingBox zone,
    /// or -1; NPCs only when <paramref name="npcRule"/> tests them like objects.
    /// </summary>
    /// <param name="slots">Reused buffer.</param>
    /// <param name="candidates">Reused buffer.</param>
    public static int FindFirstCentreInBoxZone(
        TargetObject target,
        OtherObjectIndex index,
        Replacements replacements,
        BaseObjectShapeProvider shapes,
        float multiplier,
        NpcClashRule npcRule,
        List<int> slots,
        List<int> candidates)
    {
        if (index.Count == 0) return -1;

        var position = target.Transform.Position;
        var rotation = target.Transform.Rotation;
        var expanded = Geometry.ExpandedLocalBox(shapes.GetLocalBox(target.Base), target.Transform.Scale, multiplier);
        index.Bounds.CollectCandidates(Geometry.WorldAabb(expanded, position, rotation), slots, candidates);
        foreach (var otherIndex in candidates)
        {
            if (replacements.IsReplaced(index[otherIndex].Id) || !npcRule.TestsLikeObject(index[otherIndex])) continue;
            if (index.TryGetVisibleCenter(otherIndex, out var center) && Geometry.IsInsideOrientedBox(center, position, rotation, expanded)) return otherIndex;
        }
        return -1;
    }
}
