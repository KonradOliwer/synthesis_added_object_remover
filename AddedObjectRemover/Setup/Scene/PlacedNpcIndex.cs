using Noggog;

namespace AddedObjectRemover;

/// <param name="Evaluated">Placed NPCs with a body, counted by their least precise possible body.</param>
/// <param name="WithoutNpc">Placed NPCs skipped because their base resolves to no NPC, so they never spawn.</param>
internal readonly record struct NpcSizeCounts(
    int Evaluated,
    int ByBodyMesh,
    int ByObjectBounds,
    int ByHumanoidApproximation,
    int ByPoint,
    int WithoutNpc)
{
    public static NpcSizeCounts Of(IReadOnlyCollection<NpcBodySet> bodies, int withoutNpc) => new(
        bodies.Count,
        bodies.Count(body => body.Source == NpcSizeSource.BodyMesh),
        bodies.Count(body => body.Source == NpcSizeSource.ObjectBounds),
        bodies.Count(body => body.Source == NpcSizeSource.HumanoidApproximation),
        bodies.Count(body => body.Source == NpcSizeSource.Point),
        withoutNpc);

    public NpcSizeCounts Add(NpcSizeCounts other) => new(
        Evaluated + other.Evaluated,
        ByBodyMesh + other.ByBodyMesh,
        ByObjectBounds + other.ByObjectBounds,
        ByHumanoidApproximation + other.ByHumanoidApproximation,
        ByPoint + other.ByPoint,
        WithoutNpc + other.WithoutNpc);
}

/// <summary>An other-mod placed NPC with a possible body sized as a point because its real size could not be determined.</summary>
internal readonly record struct PointNpc(OtherObject Npc, string Reason);

/// <summary>
/// The placed NPCs among one space's other objects that can spawn, with their bodies, indexed by
/// the world AABB of their combined body box. Slots are in <see cref="OtherId"/> order. Actors stand
/// upright, so a body turns only about Z. Read-only after construction and safe to query from
/// many threads at once.
/// </summary>
internal sealed class PlacedNpcIndex
{
    private readonly OtherObject[] _npcs;
    private readonly NpcBodySet[] _bodies;
    private readonly PlacedTransform[] _transforms;
    private readonly OrientedBox[] _worldBoxes;
    private readonly SpatialGrid _grid;
    private readonly PointNpc[] _pointFallbacks;

    private PlacedNpcIndex(OtherObject[] npcs, NpcBodySet[] bodies, int withoutNpc)
    {
        _npcs = npcs;
        _bodies = bodies;
        _transforms = npcs.Select(ToUprightTransform).ToArray();
        _worldBoxes = Enumerable.Range(0, npcs.Length)
            .Select(slot => OrientedBox.FromLocal(_bodies[slot].CombinedBox, _transforms[slot]))
            .ToArray();
        _grid = SpatialGrid.FromBoxes(_worldBoxes.Select(box => box.WorldAabb(0f)).ToArray());

        _pointFallbacks = Enumerable.Range(0, npcs.Length)
            .Where(slot => bodies[slot].FirstPoint != null)
            .Select(slot => new PointNpc(npcs[slot], bodies[slot].FirstPoint!.PointReason ?? string.Empty))
            .ToArray();
        Counts = NpcSizeCounts.Of(bodies, withoutNpc);
    }

    public int Count => _npcs.Length;

    public NpcSizeCounts Counts { get; }

    /// <summary>For the detailed log only: the NPCs with a point-sized possible body, with why its real size is unknown.</summary>
    public IReadOnlyList<PointNpc> PointFallbacks => _pointFallbacks;

    /// <param name="spaceObjects">The objects of one space, in <see cref="OtherId"/> order.</param>
    public static PlacedNpcIndex Build(IReadOnlyList<OtherObject> spaceObjects, NpcBodyCache bodies, ParallelOptions parallelOptions)
    {
        var placedNpcs = spaceObjects.Where(other => other.IsPlacedNpc).ToArray();
        var placedBodies = ParallelMap.Run(parallelOptions, placedNpcs.Length, i => GetBodies(placedNpcs[i], bodies), rangeSize: ParallelMap.OneItemPerRange);

        var spawning = Enumerable.Range(0, placedNpcs.Length).Where(i => placedBodies[i] != null).ToArray();
        return new PlacedNpcIndex(
            spawning.Select(i => placedNpcs[i]).ToArray(),
            spawning.Select(i => placedBodies[i]!).ToArray(),
            placedNpcs.Length - spawning.Length);
    }

    private static NpcBodySet? GetBodies(OtherObject npc, NpcBodyCache bodies) =>
        npc.Base is { } baseRef ? bodies.GetBodies(baseRef.FormKey) : null;

    public OtherObject NpcOf(int slot) => _npcs[slot];

    public NpcBodySet BodiesOf(int slot) => _bodies[slot];

    public PlacedTransform TransformOf(int slot) => _transforms[slot];

    public OrientedBox WorldBoxOf(int slot) => _worldBoxes[slot];

    /// <summary>Replaces <paramref name="slots"/> with the sorted, distinct NPC slots whose body AABB may overlap <paramref name="area"/>.</summary>
    public void Collect(Box area, List<int> slots) => _grid.CollectDistinct(area, slots);

    private static PlacedTransform ToUprightTransform(OtherObject npc) =>
        new(npc.Position, Geometry.RotationFromEuler(new P3Float(0f, 0f, npc.Rotation.Z)), npc.Scale);
}
