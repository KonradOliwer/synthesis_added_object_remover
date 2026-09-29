namespace AddedObjectRemover;

/// <summary>
/// The placed objects of the world behind the narrow views the steps read. Each space is indexed
/// on first use, and every answer depends only on the world. Thread-safe.
/// </summary>
internal sealed class Scene
{
    private readonly World _world;
    private readonly ShapeCatalog _shapes;
    private readonly NpcBodyCache _bodies;
    private readonly ObjectContainment _containment;
    private readonly Execution _execution;
    private readonly PlacedSpaces _rivals;
    private readonly Lazy<PlacedSpaces> _solids;

    private Scene(World world, ShapeCatalog shapes, TriangleStore triangles, NpcBodyCache bodies, Execution execution, IPhaseTimer timer)
    {
        _world = world;
        _shapes = shapes;
        _bodies = bodies;
        _containment = new ObjectContainment(shapes, triangles);
        _execution = execution;
        _rivals = new PlacedSpaces(world.Rivals, shapes, execution, timer, TimedPhase.RivalBoundsBuild);
        _solids = new Lazy<PlacedSpaces>(
            () => new PlacedSpaces([.. world.RivalsAndBackdrop], shapes, execution, timer, TimedPhase.SolidBoundsBuild), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public static Scene Create(World world, ShapeCatalog shapes, TriangleStore triangles, NpcBodyCache bodies, Execution execution, IPhaseTimer timer) =>
        new(world, shapes, triangles, bodies, execution, timer);

    public IRivalPositions RivalPositions() => new RivalPositions(_rivals);

    public IActiveRivals ActiveRivals(Replacements replaced, NpcHandling npcs) =>
        new ActiveRivals(
            _rivals,
            _containment,
            rival => !replaced.IsReplaced(rival.Id) && (npcs == NpcHandling.CountLikeObjects || !rival.IsPlacedNpc));

    public INpcs Npcs(Replacements replaced) => new Npcs(_rivals, _bodies, replaced, _execution);

    /// <summary>Only when the run collected the backdrop.</summary>
    public ISolids Solids() => new Solids(_solids.Value, _containment);

    /// <param name="looks">By <see cref="TargetId"/>.</param>
    public IVisibleTargets VisibleTargets(TargetLooks looks) =>
        new VisibleTargets(_world.Targets, looks, _shapes, _containment);

    /// <param name="looks">By <see cref="TargetId"/>.</param>
    public RivalCensus Census(TargetLooks looks)
    {
        var targetSpaces = _world.Targets.Select(target => target.SpaceKey).ToHashSet();
        var visibleTargetSpaces = _world.Targets
            .Where(target => looks.ByTarget[target.Id.Index].IsVisible)
            .Select(target => target.SpaceKey)
            .ToHashSet();
        var invisibleByReason = _world.Rivals
            .Where(rival => visibleTargetSpaces.Contains(rival.SpaceKey))
            .Select(rival => _shapes.GetVisibility(rival.Base, rival.IsPrimitive, rival.HasMapMarker))
            .Where(rivalVisibility => !rivalVisibility.IsVisible)
            .GroupBy(rivalVisibility => rivalVisibility.Describe(), StringComparer.Ordinal)
            .Select(group => KeyValuePair.Create(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
        var placedNpcs = _world.Rivals.Count(rival => targetSpaces.Contains(rival.SpaceKey) && rival.IsPlacedNpc);
        return new RivalCensus(invisibleByReason, placedNpcs);
    }
}
