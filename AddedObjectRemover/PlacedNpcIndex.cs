namespace AddedObjectRemover;

internal readonly record struct NpcSizeCounts(int Evaluated, int ByBodyMesh, int ByObjectBounds, int ByHumanoidApproximation, int ByPoint)
{
    public static NpcSizeCounts Of(IReadOnlyCollection<NpcBody> bodies) => new(
        bodies.Count,
        bodies.Count(body => body.Source == NpcSizeSource.BodyMesh),
        bodies.Count(body => body.Source == NpcSizeSource.ObjectBounds),
        bodies.Count(body => body.Source == NpcSizeSource.HumanoidApproximation),
        bodies.Count(body => body.Source == NpcSizeSource.Point));

    public NpcSizeCounts Add(NpcSizeCounts other) => new(
        Evaluated + other.Evaluated,
        ByBodyMesh + other.ByBodyMesh,
        ByObjectBounds + other.ByObjectBounds,
        ByHumanoidApproximation + other.ByHumanoidApproximation,
        ByPoint + other.ByPoint);
}

/// <summary>An other-mod placed NPC sized as a point because its real size could not be determined.</summary>
internal readonly record struct PointNpc(OtherObject Npc, string Reason);

/// <summary>
/// The placed NPCs among one space's other objects with their bodies, indexed by the world AABB of
/// their body (a single point for a point-sized NPC). Slots are in other-object index order.
/// Read-only after construction and safe to query from many threads at once.
/// </summary>
internal sealed class PlacedNpcIndex
{
    private readonly int[] _others;
    private readonly NpcBody[] _bodies;
    private readonly PlacedTransform[] _transforms;
    private readonly OrientedBox[] _worldBoxes;
    private readonly SpatialGrid _grid;
    private readonly PointNpc[] _pointFallbacks;

    private PlacedNpcIndex(OtherObjectIndex others, int[] npcOthers, NpcBody[] bodies)
    {
        _others = npcOthers;
        _bodies = bodies;
        _transforms = Enumerable.Range(0, npcOthers.Length).Select(i => ToBodyTransform(others[npcOthers[i]], bodies[i])).ToArray();
        _worldBoxes = Enumerable.Range(0, npcOthers.Length)
            .Select(slot => OrientedBox.FromLocal(_bodies[slot].LocalBox, _transforms[slot]))
            .ToArray();
        _grid = SpatialGrid.FromBoxes(_worldBoxes.Select(box => box.WorldAabb(0f)).ToArray());

        _pointFallbacks = Enumerable.Range(0, npcOthers.Length)
            .Where(i => bodies[i].Source == NpcSizeSource.Point)
            .Select(i => new PointNpc(others[npcOthers[i]], bodies[i].PointReason ?? string.Empty))
            .ToArray();
        Counts = NpcSizeCounts.Of(bodies);
    }

    public NpcSizeCounts Counts { get; }

    /// <summary>For the detailed log only: the NPCs sized as a point, with why their real size is unknown.</summary>
    public IReadOnlyList<PointNpc> PointFallbacks => _pointFallbacks;

    public static PlacedNpcIndex Build(OtherObjectIndex others, NpcBodyCache bodies, ParallelOptions parallelOptions)
    {
        var npcOthers = Enumerable.Range(0, others.Count).Where(i => others[i].IsPlacedNpc).ToArray();
        var npcBodies = new NpcBody[npcOthers.Length];
        Parallel.For(0, npcOthers.Length, parallelOptions, i => npcBodies[i] = GetBody(others[npcOthers[i]], bodies));
        return new PlacedNpcIndex(others, npcOthers, npcBodies);
    }

    private static NpcBody GetBody(OtherObject npc, NpcBodyCache bodies) =>
        npc.Base is { } baseRef ? bodies.GetBody(baseRef.FormKey) : NpcBody.Point("no base NPC");

    public int OtherIndexOf(int slot) => _others[slot];

    public NpcBody BodyOf(int slot) => _bodies[slot];

    public PlacedTransform TransformOf(int slot) => _transforms[slot];

    public OrientedBox WorldBoxOf(int slot) => _worldBoxes[slot];

    /// <summary>Replaces <paramref name="slots"/> with the sorted, distinct NPC slots whose body AABB may overlap <paramref name="area"/>.</summary>
    public void Collect(Box area, List<int> slots)
    {
        slots.Clear();
        _grid.Collect(area, slots);
        slots.Sort();
        OtherObjectBoxIndex.RemoveAdjacentDuplicates(slots);
    }

    /// <summary>The body placed at the reference, scaled by the reference scale times the body's height scale.</summary>
    private static PlacedTransform ToBodyTransform(OtherObject npc, NpcBody body)
    {
        var placed = npc.Transform;
        return placed with { Scale = placed.Scale * body.HeightScale };
    }
}
