using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal sealed class RivalPositions(PlacedSpaces rivals) : IRivalPositions
{
    public void Within(FormKey space, Vector3 point, float radius, List<OtherId> into)
    {
        into.Clear();
        var index = rivals.IndexOf(space);
        var slots = new List<int>();
        index.PositionGrid.CollectDistinct(new Box(point, point).Grown(radius), slots);
        foreach (var slot in slots)
        {
            if (Vector3.Distance(index[slot].Position, point) > radius || !index.IsVisible(slot)) continue;
            into.Add(index[slot].Id);
        }
    }

    public OtherObject Get(OtherId id) => rivals.Get(id);
}

/// <param name="isActive">Whether a rival takes part, visibility aside.</param>
internal sealed class ActiveRivals(PlacedSpaces rivals, ObjectContainment containment, Func<OtherObject, bool> isActive) : IActiveRivals
{
    public int Overlapping(FormKey space, Box area, SpatialQueryScratch scratch, List<OtherId> into)
    {
        into.Clear();
        var index = rivals.IndexOf(space);
        index.Bounds.CollectCandidates(area, scratch.Slots, scratch.Candidates);
        foreach (var slot in scratch.Candidates)
        {
            if (isActive(index[slot]) && index.IsVisible(slot)) into.Add(index[slot].Id);
        }
        return scratch.Candidates.Count;
    }

    public OtherId? FirstCovering(FormKey space, Vector3 point, SpatialQueryScratch scratch)
    {
        var index = rivals.IndexOf(space);
        var slot = containment.FindContainingVisible(index, point, isActive, scratch);
        return slot >= 0 ? index[slot].Id : null;
    }

    public OtherObject Get(OtherId id) => rivals.Get(id);

    public Vector3 CentreOf(OtherId id)
    {
        var (index, slot) = rivals.Locate(id);
        return index.TryGetVisibleCenter(slot, out var centre)
            ? centre
            : throw new InvalidOperationException($"Rival {rivals.Get(id).FormKey} is invisible, so it is not active.");
    }

    public int LargeObjectCount(FormKey space) => rivals.IndexOf(space).Bounds.LargeObjectCount;
}

internal sealed class Npcs(PlacedSpaces rivals, NpcBodyCache bodies, Replacements replaced, ParallelOptions parallelOptions) : INpcs
{
    private readonly LazyCache<FormKey, PlacedNpcIndex> _bySpace = new();

    public void Overlapping(FormKey space, Box area, List<int> slots)
    {
        var npcs = IndexOf(space);
        npcs.Collect(area, slots);
        slots.RemoveAll(slot => replaced.IsReplaced(npcs.NpcOf(slot).Id));
    }

    public OtherObject NpcOf(FormKey space, int slot) => IndexOf(space).NpcOf(slot);

    public NpcBodySet BodiesOf(FormKey space, int slot) => IndexOf(space).BodiesOf(slot);

    public PlacedTransform TransformOf(FormKey space, int slot) => IndexOf(space).TransformOf(slot);

    public OrientedBox WorldBoxOf(FormKey space, int slot) => IndexOf(space).WorldBoxOf(slot);

    public NpcSizeCounts SizesIn(FormKey space) => IndexOf(space).Counts;

    public IReadOnlyList<PointNpc> PointFallbacksIn(FormKey space) => IndexOf(space).PointFallbacks;

    private PlacedNpcIndex IndexOf(FormKey space) =>
        _bySpace.GetOrCreate(space, () => PlacedNpcIndex.Build(rivals.ObjectsIn(space), bodies, parallelOptions));
}

internal sealed class Solids(PlacedSpaces solids, ObjectContainment containment) : ISolids
{
    public void Overlapping(FormKey space, Box area, SpatialQueryScratch scratch, List<OtherId> into)
    {
        into.Clear();
        var index = solids.IndexOf(space);
        index.Bounds.CollectCandidates(area, scratch.Slots, scratch.Candidates);
        foreach (var slot in scratch.Candidates)
        {
            if (index.IsVisible(slot)) into.Add(index[slot].Id);
        }
    }

    public bool Contains(FormKey space, Vector3 point, SpatialQueryScratch scratch) =>
        containment.FindContainingVisible(solids.IndexOf(space), point, include: _ => true, scratch) >= 0;

    public OtherObject Get(OtherId id) => solids.Get(id);
}
