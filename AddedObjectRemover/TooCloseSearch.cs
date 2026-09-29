namespace AddedObjectRemover;

/// <summary>Work counts of the too-close search: the ObjectShape zone's (zero for the BoundingBox zone) and the NPC-stuck search's.</summary>
internal readonly record struct ClashWork(ShapeZoneWork Zone, NpcWork Npcs) : IWork<ClashWork>
{
    public static ClashWork Zero => default;

    public static ClashWork operator +(ClashWork a, ClashWork b) => new(a.Zone + b.Zone, a.Npcs + b.Npcs);
}

/// <param name="Hits">In target order.</param>
internal sealed record ClashSearchResult(List<TooCloseHit> Hits, ClashWork Work);

/// <summary>
/// BoundingBox removal zone: finds visible target objects whose grown (multiplier-expanded) local
/// box contains the bounds center of some active rival. Other mods' placed NPCs follow the
/// <see cref="NpcClashRule"/>. Invisible targets are left to the leftover invisible objects step.
/// </summary>
internal static class TooCloseSearch
{
    /// <summary>Reusable buffers and counters of one worker thread.</summary>
    private sealed class Scratch
    {
        public SpatialQueryScratch Query { get; } = new();
        public List<OtherId> Candidates { get; } = [];
        public NpcScratch Npcs { get; } = new();

        public ClashWork Harvest() => new(ShapeZoneWork.Zero, Npcs.Harvest());
    }

    /// <summary>Candidates are tested in index order, so which rival is reported does not depend on thread scheduling.</summary>
    /// <param name="looks">Parallel to <paramref name="targets"/>.</param>
    public static ClashSearchResult FindTooCloseTargets(
        IReadOnlyList<TargetObject> targets,
        TargetLooks looks,
        IActiveRivals rivals,
        ShapeCatalog shapes,
        float multiplier,
        NpcClashRule npcRule,
        WorkOrder order,
        Execution execution)
    {
        var (matches, work) = ParallelMap.Run(
            execution,
            order,
            targets.Count,
            () => new Scratch(),
            (targetIndex, scratch) => looks.ByTarget[targetIndex].IsVisible
                ? FindFirstTooCloseOther(targets[targetIndex], targetIndex, rivals, shapes, multiplier, npcRule, scratch)
                : null,
            scratch => scratch.Harvest());
        return new ClashSearchResult(ToHits(rivals, matches), work);
    }

    /// <param name="matches">Per target, the rival it is too close to, or null.</param>
    /// <returns>The hits in target order.</returns>
    public static List<TooCloseHit> ToHits(IActiveRivals rivals, IReadOnlyList<OtherId?> matches)
    {
        var hits = new List<TooCloseHit>();
        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i] is { } rival) hits.Add(new TooCloseHit(i, rivals.Get(rival)));
        }
        return hits;
    }

    private static OtherId? FindFirstTooCloseOther(
        TargetObject target, int targetIndex, IActiveRivals rivals, ShapeCatalog shapes, float multiplier, NpcClashRule npcRule, Scratch scratch) =>
        npcRule.ThenFirstStuckNpc(
            FindFirstCentreInBoxZone(target, rivals, shapes, multiplier, scratch.Query, scratch.Candidates), targetIndex, scratch.Npcs);

    /// <summary>The lowest active rival whose bounds centre lies in the target's BoundingBox zone, or null.</summary>
    /// <param name="candidates">Reused buffer.</param>
    public static OtherId? FindFirstCentreInBoxZone(
        TargetObject target,
        IActiveRivals rivals,
        ShapeCatalog shapes,
        float multiplier,
        SpatialQueryScratch scratch,
        List<OtherId> candidates)
    {
        var position = target.Transform.Position;
        var rotation = target.Transform.Rotation;
        var expanded = Geometry.ExpandedLocalBox(shapes.GetLocalBox(target.Base), target.Transform.Scale, multiplier);
        rivals.Overlapping(target.SpaceKey, Geometry.WorldAabb(expanded, position, rotation), scratch, candidates);
        foreach (var rival in candidates)
        {
            if (Geometry.IsInsideOrientedBox(rivals.CentreOf(rival), position, rotation, expanded)) return rival;
        }
        return null;
    }
}
