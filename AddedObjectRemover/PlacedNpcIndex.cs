namespace AddedObjectRemover;

internal readonly record struct NpcSizeCounts(int Evaluated, int ByBodyMesh, int ByObjectBounds, int ByHumanoidApproximation, int Skipped)
{
    public static NpcSizeCounts Of(IReadOnlyCollection<NpcBody> bodies) => new(
        bodies.Count,
        bodies.Count(body => body.Source == NpcSizeSource.BodyMesh),
        bodies.Count(body => body.Source == NpcSizeSource.ObjectBounds),
        bodies.Count(body => body.Source == NpcSizeSource.HumanoidApproximation),
        bodies.Count(body => body.Source == NpcSizeSource.Unknown));

    public NpcSizeCounts Add(NpcSizeCounts other) => new(
        Evaluated + other.Evaluated,
        ByBodyMesh + other.ByBodyMesh,
        ByObjectBounds + other.ByObjectBounds,
        ByHumanoidApproximation + other.ByHumanoidApproximation,
        Skipped + other.Skipped);
}

/// <summary>An other-mod placed NPC whose size could not be worked out, so it never causes a removal.</summary>
internal readonly record struct UnsizedNpc(OtherObject Npc, string Reason);

/// <summary>
/// The placed NPCs among one space's other objects with their bodies. Sized NPCs are indexed by
/// the world AABB of their body, NPCs of unknown size by their position. Slots are in other-object
/// index order. Read-only after construction and safe to query from many threads at once.
/// </summary>
internal sealed class PlacedNpcIndex
{
    private readonly int[] _sizedOthers;
    private readonly NpcBody[] _sizedBodies;
    private readonly PlacedTransform[] _sizedTransforms;
    private readonly OrientedBox[] _sizedWorldBoxes;
    private readonly SpatialGrid _sizedGrid;
    private readonly UnsizedNpc[] _unsized;
    private readonly SpatialGrid _unsizedGrid;

    private PlacedNpcIndex(OtherObjectIndex others, int[] npcOthers, NpcBody[] bodies)
    {
        var sized = Enumerable.Range(0, npcOthers.Length).Where(i => bodies[i].IsSized).ToArray();
        _sizedOthers = sized.Select(i => npcOthers[i]).ToArray();
        _sizedBodies = sized.Select(i => bodies[i]).ToArray();
        _sizedTransforms = sized.Select(i => ToBodyTransform(others[npcOthers[i]], bodies[i])).ToArray();
        _sizedWorldBoxes = Enumerable.Range(0, sized.Length)
            .Select(slot => OrientedBox.FromLocal(_sizedBodies[slot].LocalBox, _sizedTransforms[slot]))
            .ToArray();
        _sizedGrid = SpatialGrid.FromBoxes(_sizedWorldBoxes.Select(box => box.WorldAabb(0f)).ToArray());

        _unsized = Enumerable.Range(0, npcOthers.Length)
            .Where(i => !bodies[i].IsSized)
            .Select(i => new UnsizedNpc(others[npcOthers[i]], bodies[i].UnknownReason ?? string.Empty))
            .ToArray();
        _unsizedGrid = SpatialGrid.FromPoints(_unsized.Select(unsized => unsized.Npc.Position).ToArray());
        Counts = NpcSizeCounts.Of(bodies);
    }

    public NpcSizeCounts Counts { get; }

    public IReadOnlyList<UnsizedNpc> Unsized => _unsized;

    public static PlacedNpcIndex Build(OtherObjectIndex others, NpcBodyCache bodies, ParallelOptions parallelOptions)
    {
        var npcOthers = Enumerable.Range(0, others.Count).Where(i => others[i].IsPlacedNpc).ToArray();
        var npcBodies = new NpcBody[npcOthers.Length];
        Parallel.For(0, npcOthers.Length, parallelOptions, i => npcBodies[i] = GetBody(others[npcOthers[i]], bodies));
        return new PlacedNpcIndex(others, npcOthers, npcBodies);
    }

    private static NpcBody GetBody(OtherObject npc, NpcBodyCache bodies) =>
        npc.Base is { } baseRef ? bodies.GetBody(baseRef.FormKey) : NpcBody.Unknown("no base NPC");

    public int OtherIndexOf(int slot) => _sizedOthers[slot];

    public NpcBody BodyOf(int slot) => _sizedBodies[slot];

    public PlacedTransform TransformOf(int slot) => _sizedTransforms[slot];

    public OrientedBox WorldBoxOf(int slot) => _sizedWorldBoxes[slot];

    /// <summary>Replaces <paramref name="slots"/> with the sorted, distinct sized NPC slots whose body AABB may overlap <paramref name="area"/>.</summary>
    public void CollectSized(Box area, List<int> slots)
    {
        slots.Clear();
        _sizedGrid.Collect(area, slots);
        slots.Sort();
        OtherObjectBoxIndex.RemoveAdjacentDuplicates(slots);
    }

    /// <summary>NPCs of unknown size standing (by their placement point) inside <paramref name="box"/>, in index order.</summary>
    /// <param name="slots">Reused buffer.</param>
    public List<UnsizedNpc> CollectUnsizedInside(OrientedBox box, List<int> slots)
    {
        slots.Clear();
        _unsizedGrid.Collect(box.WorldAabb(0f), slots);
        slots.Sort();
        OtherObjectBoxIndex.RemoveAdjacentDuplicates(slots);
        return slots.Where(slot => box.Contains(_unsized[slot].Npc.Position)).Select(slot => _unsized[slot]).ToList();
    }

    /// <summary>The body placed at the reference, scaled by the reference scale times the body's height scale.</summary>
    private static PlacedTransform ToBodyTransform(OtherObject npc, NpcBody body)
    {
        var placed = npc.Transform;
        return placed with { Scale = placed.Scale * body.HeightScale };
    }
}
