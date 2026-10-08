using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The placed NPCs among one space's other objects that can spawn, with their bodies, indexed by
/// the world AABB of their combined body box. Slots are in <see cref="OtherId"/> order. Actors stand
/// upright, so a body turns only about Z. Read-only after construction and safe to query from
/// many threads at once.
/// </summary>
internal sealed class PlacedNpcIndex : IPlacedNpcIndex
{
    private readonly OtherObject[] _npcs;
    private readonly NpcBodySet[] _bodies;
    private readonly PlacedTransform[] _transforms;
    private readonly OrientedBox[] _worldBoxes;
    private readonly GridCandidates _grid;
    private readonly PointNpc[] _pointFallbacks;

    private PlacedNpcIndex(OtherObject[] npcs, NpcBodySet[] bodies, int withoutNpc)
    {
        _npcs = npcs;
        _bodies = bodies;
        _transforms = npcs.Select(npc => PlacedTransform.Upright(npc.Position, npc.Rotation.Z, npc.Scale)).ToArray();
        _worldBoxes = Enumerable.Range(0, npcs.Length)
            .Select(slot => OrientedBox.FromLocal(_bodies[slot].CombinedBox, _transforms[slot]))
            .ToArray();
        _grid = GridCandidates.OfBoxes(_worldBoxes.Select(box => box.WorldAabb(0f)).ToArray());

        _pointFallbacks = Enumerable.Range(0, npcs.Length)
            .Where(slot => bodies[slot].FirstPoint != null)
            .Select(slot => new PointNpc(npcs[slot], bodies[slot].FirstPoint!.PointReason!.Value))
            .ToArray();
        Counts = NpcSizeCounts.Of(bodies, withoutNpc);
    }

    public NpcSizeCounts Counts { get; }

    /// <summary>For the detailed log only: the NPCs with a point-sized possible body, with why its real size is unknown.</summary>
    public IReadOnlyList<PointNpc> PointFallbacks => _pointFallbacks;

    /// <param name="spaceObjects">The objects of one space, in <see cref="OtherId"/> order.</param>
    public static PlacedNpcIndex Build(IReadOnlyList<OtherObject> spaceObjects, INpcBodies bodies, Execution execution)
    {
        var placedNpcs = spaceObjects.Where(other => other.IsPlacedNpc).ToArray();
        var placedBodies = ParallelMap.Run(execution, placedNpcs.Length, i => GetBodies(placedNpcs[i], bodies), rangeSize: ParallelMap.OneItemPerRange);

        var spawning = ParallelResults.IndicesWhere(placedBodies, body => body != null);
        return new PlacedNpcIndex(
            spawning.Select(i => placedNpcs[i]).ToArray(),
            ParallelResults.Compact(placedBodies).ToArray(),
            placedNpcs.Length - spawning.Count);
    }

    private static NpcBodySet? GetBodies(OtherObject npc, INpcBodies bodies) =>
        npc.Base is { } baseKey ? bodies.GetBodies(baseKey.Record) : null;

    public OtherObject NpcOf(int slot) => _npcs[slot];

    public NpcBodySet BodiesOf(int slot) => _bodies[slot];

    public PlacedTransform TransformOf(int slot) => _transforms[slot];

    public OrientedBox WorldBoxOf(int slot) => _worldBoxes[slot];

    /// <summary>Replaces <paramref name="slots"/> with the sorted, distinct NPC slots whose body AABB may overlap <paramref name="area"/>.</summary>
    public void Collect(Box area, List<int> slots) => _grid.CollectNear(area, slots);
}
