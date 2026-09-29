using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>An other-mod record skipped because the target plugin overrides it; listed in the verbose log.</summary>
internal readonly record struct OverriddenOtherRecord(FormKey FormKey, string? EditorId, ModKey WinningMod);

/// <summary>
/// The spaces (worldspace or interior cell FormKeys) that contain a record created by the target
/// plugin, and every placed FormKey from another plugin that the target plugin overrides.
/// </summary>
internal sealed record TargetPluginFootprint(HashSet<FormKey> SpaceKeys, HashSet<FormKey> OverriddenFormKeys);

/// <summary>
/// Collects target objects and other mods' objects from every winning placed record (REFR, ACHR
/// and all placed trap/hazard/projectile types), the records that link to target objects, for
/// Anchoring and moving kept markers also every other placed object and the terrain of the target
/// worldspaces, and for moving kept markers the navmeshes of the target spaces.
///
/// Walks each mod's cell tree once via <c>EnumerateMajorRecordContexts&lt;ICell&gt;</c> (enumerating
/// IPlaced walks each tree three times) and reads each cell's Persistent and Temporary lists itself.
/// Mods are visited highest priority first, so the first sighting of a FormKey (record or cell)
/// is its winner. The objects are then put in <see cref="FormKeyOrder"/>, which gives them their ids.
/// </summary>
internal sealed class PlacedRecordScanner
{
    private enum RecordRole { Target, TargetOverriddenLater, IgnoredOrigin, OverriddenByTarget, Other }

    /// <summary>A target object found by the walk, before it gets its id.</summary>
    private sealed record FoundTarget(TargetObject Target, IPlacedGetter Record, TargetLocation Location);

    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly ModKey _target;
    private readonly ReadPlan _plan;
    private readonly ModTable _mods;
    private readonly ModStanding _standing;
    private readonly TargetPluginFootprint _footprint;
    private readonly HashSet<FormKey> _landWorldspaces;
    private readonly HashSet<FormKey> _seenRecords = new();
    private readonly HashSet<FormKey> _seenNavmeshes = new();
    private readonly Dictionary<FormKey, IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter>> _winningCells = new();

    private readonly List<FoundTarget> _targets = [];
    private readonly List<OtherObject> _rivals = [];
    private readonly List<OtherObject> _backdrop = [];
    private readonly Dictionary<FormKey, KeepReason> _references = new();
    private readonly List<TargetPluginLink> _links = [];
    private readonly Dictionary<FormKey, string> _spaceNames = new();
    private readonly List<OverriddenOtherRecord> _overriddenOthers = [];
    private readonly Dictionary<FormKey, FormKey> _landWorldspaceOf = new();
    private readonly Dictionary<ExteriorCell, ILandscapeGetter> _landscapes = new();
    private readonly Dictionary<FormKey, List<CellNavmesh>> _navmeshes = new();

    private int _recordsScanned;
    private int _targetsOverriddenLater;
    private int _targetsHiddenOrWithoutPlacement;
    private int _othersOverriddenByTarget;
    private int _otherInvalidPlacements;

    private PlacedRecordScanner(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        ModKey target,
        ReadPlan plan,
        ModTable mods,
        ModStanding standing,
        TargetPluginFootprint footprint)
    {
        _state = state;
        _target = target;
        _plan = plan;
        _mods = mods;
        _standing = standing;
        _footprint = footprint;
        if (plan.Terrain) FindLandWorldspaces();
        _landWorldspaces = _landWorldspaceOf.Values.ToHashSet();
    }

    public static GameSnapshot Read(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, ModKey target, ReadPlan plan, ModTable mods, ModStanding standing)
    {
        var scanner = new PlacedRecordScanner(state, target, plan, mods, standing, AnalyzeTargetPlugin(state, target));
        scanner.ScanLoadOrder();
        scanner.CollectNonPlacedReferences();
        return scanner.CreateSnapshot();
    }

    private void CollectNonPlacedReferences()
    {
        var referencingMods = _state.LoadOrder.PriorityOrder
            .Select(listing => listing.Mod)
            .OfType<ISkyrimModGetter>()
            .Where(MayReferenceTarget);
        var targets = _targets.Select(found => found.Target.Key).ToHashSet();
        TargetReferenceCollector.CollectFromNonPlaced(referencingMods, targets, _references);
    }

    private void FindLandWorldspaces()
    {
        foreach (var spaceKey in _footprint.SpaceKeys)
        {
            if (!_state.LinkCache.TryResolve<IWorldspaceGetter>(spaceKey, out var worldspace)) continue;
            _landWorldspaceOf[spaceKey] = FollowLandDataParents(worldspace);
        }
    }

    /// <remarks>A parent chain that loops back (broken data) stops at the last worldspace not yet seen.</remarks>
    private FormKey FollowLandDataParents(IWorldspaceGetter worldspace)
    {
        var seen = new HashSet<FormKey> { worldspace.FormKey };
        while (worldspace.Parent is { } parent
               && parent.Flags.HasFlag(WorldspaceParent.Flag.UseLandData)
               && _state.LinkCache.TryResolve<IWorldspaceGetter>(parent.Worldspace.FormKey, out var parentWorldspace)
               && seen.Add(parentWorldspace.FormKey))
        {
            worldspace = parentWorldspace;
        }
        return worldspace.FormKey;
    }

    private void ScanLoadOrder()
    {
        foreach (var listing in _state.LoadOrder.PriorityOrder)
        {
            if (listing.Mod is not { } mod) continue;
            var collectReferences = MayReferenceTarget(mod);
            foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_state.LinkCache))
            {
                ScanCell(cellContext, listing.ModKey, collectReferences);
            }
        }
    }

    /// <summary>A plugin can only link to a target FormKey if it is the target, has it as a master, or is the patch.</summary>
    private bool MayReferenceTarget(ISkyrimModGetter mod) =>
        mod.ModKey == _target
        || mod.ModKey == _state.PatchMod.ModKey
        || mod.MasterReferences.Any(master => master.Master == _target);

    /// <summary>
    /// Walks only the target plugin's own cell tree. A FormKey the target overrides is a
    /// replacement, never an "other mod" object, whichever plugin wins it.
    /// </summary>
    private static TargetPluginFootprint AnalyzeTargetPlugin(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, ModKey target)
    {
        var footprint = new TargetPluginFootprint([], []);
        if (state.LoadOrder.ListedOrder.FirstOrDefault(listing => listing.ModKey == target)?.Mod is not { } mod) return footprint;

        foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(state.LinkCache))
        {
            var hasTargetRecord = false;
            foreach (var (record, _) in cellContext.Record.EnumeratePlaced())
            {
                if (record.FormKey.ModKey == target) hasTargetRecord = true;
                else footprint.OverriddenFormKeys.Add(record.FormKey);
            }
            if (hasTargetRecord) footprint.SpaceKeys.Add(GetCellSpace(cellContext).SpaceKey);
        }
        return footprint;
    }

    private void ScanCell(
        IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext,
        ModKey winningMod,
        bool collectReferences)
    {
        var cell = cellContext.Record;
        var space = GetCellSpace(cellContext);
        var inTargetSpace = _footprint.SpaceKeys.Contains(space.SpaceKey);
        if (inTargetSpace) _winningCells.TryAdd(cell.FormKey, cellContext);
        if (_landWorldspaces.Contains(space.SpaceKey)) CollectLandscape(cell, space);
        if (_plan.Navmesh && inTargetSpace) CollectNavmeshes(cell, space);

        foreach (var (record, persistent) in cell.EnumeratePlaced())
        {
            ScanRecord(record, persistent, cell, space, inTargetSpace, winningMod, collectReferences);
        }
    }

    /// <remarks>
    /// LAND is a record of its own inside the cell, so a cell override without it leaves the
    /// terrain to a lower-priority plugin; the first cell copy found that carries one is the winner.
    /// </remarks>
    private void CollectLandscape(ICellGetter cell, CellSpace space)
    {
        if (space.SpaceKey == cell.FormKey || cell.Grid is not { } grid || cell.Landscape is not { } landscape) return;
        _landscapes.TryAdd(new ExteriorCell(space.SpaceKey, grid.Point.X, grid.Point.Y), landscape);
    }

    /// <remarks>Like placed records, the first copy of a navmesh found is its winner.</remarks>
    private void CollectNavmeshes(ICellGetter cell, CellSpace space)
    {
        (int X, int Y)? grid = space.SpaceKey != cell.FormKey && cell.Grid is { } cellGrid ? (cellGrid.Point.X, cellGrid.Point.Y) : null;
        foreach (var navmesh in cell.NavigationMeshes)
        {
            if (!_seenNavmeshes.Add(navmesh.FormKey) || navmesh.IsDeleted || navmesh.Data is not { } data) continue;
            GetOrAddSpaceList(_navmeshes, space.SpaceKey).Add(new CellNavmesh(grid, data));
        }
    }

    private void ScanRecord(
        IPlacedGetter record,
        bool persistent,
        ICellGetter cell,
        CellSpace space,
        bool inTargetSpace,
        ModKey winningMod,
        bool collectReferences)
    {
        if (!_seenRecords.Add(record.FormKey)) return;
        _recordsScanned++;
        if (record.IsDeleted) return;

        var role = Classify(record, winningMod);
        // Targets define the target spaces, so no placement outside them can matter.
        var presence = inTargetSpace ? PresenceRule.Of(ReadPlacementFacts(record), isWinningTarget: role == RecordRole.Target) : Presence.Hidden;
        var placement = presence == Presence.Present ? record.Placement : null;
        if (collectReferences)
        {
            var isTargetObject = role == RecordRole.Target && placement != null;
            TargetReferenceCollector.CollectFromPlaced(record, isTargetObject, _target, _references, _links);
        }

        if (_plan.Backdrop && role is not (RecordRole.Target or RecordRole.Other) && placement != null)
        {
            _backdrop.Add(CreateOtherObject(record, placement, space.SpaceKey, winningMod, editorId: null));
        }
        if (CountIfSkipped(role, record, winningMod) || !inTargetSpace) return;

        if (placement == null)
        {
            if (role == RecordRole.Target) _targetsHiddenOrWithoutPlacement++;
            else if (presence == Presence.InvalidPlacement) _otherInvalidPlacements++;
            return;
        }

        if (role == RecordRole.Target) AddTarget(record, persistent, placement, cell, space);
        else _rivals.Add(CreateOtherObject(record, placement, space.SpaceKey, winningMod, _plan.OtherEditorIds ? record.EditorID : null));
    }

    /// <returns>True when the record is neither a target nor another mod's object.</returns>
    private bool CountIfSkipped(RecordRole role, IPlacedGetter record, ModKey winningMod)
    {
        switch (role)
        {
            case RecordRole.TargetOverriddenLater:
                _targetsOverriddenLater++;
                return true;
            case RecordRole.IgnoredOrigin:
                return true;
            case RecordRole.OverriddenByTarget:
                _othersOverriddenByTarget++;
                if (_plan.OverriddenOthersList) _overriddenOthers.Add(new OverriddenOtherRecord(record.FormKey, record.EditorID, winningMod));
                return true;
            default:
                return false;
        }
    }

    private static PlacementFacts ReadPlacementFacts(IPlacedGetter record) => new(
        record.IsInitiallyDisabled(),
        record.EnableParent != null,
        record.Placement is { } placement ? Geometry.ToVector(placement.Position) : null,
        record.Placement is { } rotated ? Geometry.ToVector(rotated.Rotation) : null);

    private RecordRole Classify(IPlacedGetter record, ModKey winningMod)
    {
        var origin = record.FormKey.ModKey;
        if (origin == _target)
        {
            // Also true when an earlier patcher in this run (the patch mod) overrode it.
            return winningMod == _target ? RecordRole.Target : RecordRole.TargetOverriddenLater;
        }
        if (_mods.Find(origin) is { } originMod && _standing.IgnoredOrigins.Contains(originMod)) return RecordRole.IgnoredOrigin;
        return _footprint.OverriddenFormKeys.Contains(record.FormKey) ? RecordRole.OverriddenByTarget : RecordRole.Other;
    }

    private void AddTarget(IPlacedGetter record, bool persistent, IPlacementGetter placement, ICellGetter cell, CellSpace space)
    {
        if (!_spaceNames.ContainsKey(space.SpaceKey))
        {
            _spaceNames[space.SpaceKey] = RecordNames.DescribeSpace(space.SpaceRecord);
        }

        var target = new TargetObject(
            Id: default,
            Key: record.FormKey,
            EditorId: record.EditorID,
            SpaceKey: space.SpaceKey,
            CellName: cell.FormKey != space.SpaceKey ? RecordNames.Describe(cell) : null,
            Transform: new PlacedTransform(
                Geometry.ToVector(placement.Position),
                Geometry.RotationFromEuler(placement.Rotation),
                Geometry.NormalizeScale(record.Scale)),
            Rotation: placement.Rotation,
            Base: record.GetBaseRef(),
            IsTeleportDoor: record is IPlacedObjectGetter { TeleportDestination: not null },
            IsPrimitive: record is IPlacedObjectGetter { Primitive: not null },
            HasMapMarker: record is IPlacedObjectGetter { MapMarker: not null },
            OwnReach: InvisibleObjectReach.GetReferenceReach(record));
        _targets.Add(new FoundTarget(target, record, new TargetLocation(_winningCells[cell.FormKey], persistent)));
    }

    private static List<T> GetOrAddSpaceList<T>(Dictionary<FormKey, List<T>> bySpace, FormKey spaceKey)
    {
        if (!bySpace.TryGetValue(spaceKey, out var items))
        {
            items = [];
            bySpace[spaceKey] = items;
        }
        return items;
    }

    private static OtherObject CreateOtherObject(IPlacedGetter record, IPlacementGetter placement, FormKey spaceKey, ModKey winningMod, string? editorId) => new(
        default,
        record.FormKey,
        spaceKey,
        winningMod,
        editorId,
        record.GetBaseRef(),
        Geometry.ToVector(placement.Position),
        placement.Rotation,
        Geometry.NormalizeScale(record.Scale),
        record is IPlacedObjectGetter { Primitive: not null },
        record is IPlacedObjectGetter { MapMarker: not null });

    private GameSnapshot CreateSnapshot()
    {
        var targets = _targets.OrderBy(found => found.Target.Key, FormKeyOrder.Comparer).ToList();
        var world = new World(
            targets.Select((found, index) => found.Target with { Id = new TargetId(index) }).ToImmutableArray(),
            NumberInFormKeyOrder(_rivals, firstId: 0),
            _plan.Backdrop
                ? Collected<ImmutableArray<OtherObject>>.Of(NumberInFormKeyOrder(_backdrop, firstId: _rivals.Count))
                : Collected<ImmutableArray<OtherObject>>.NotCollected,
            ResolveLinks(targets),
            targets.Select(found => _references.GetValueOrDefault(found.Target.Key)).ToImmutableArray(),
            _spaceNames,
            new ReadCounts(
                _recordsScanned,
                _targetsOverriddenLater,
                _targetsHiddenOrWithoutPlacement,
                _othersOverriddenByTarget,
                _otherInvalidPlacements,
                _links.Count,
                _navmeshes.Values.Sum(navmeshes => navmeshes.Count)),
            [.. _overriddenOthers]);
        return new GameSnapshot(
            world,
            new RecordHandles([.. targets.Select(found => found.Record)], [.. targets.Select(found => found.Location)]),
            new TerrainHeights(_landscapes, _landWorldspaceOf),
            _navmeshes);
    }

    private static ImmutableArray<OtherObject> NumberInFormKeyOrder(IEnumerable<OtherObject> others, int firstId) =>
        others
            .OrderBy(other => other.FormKey, FormKeyOrder.Comparer)
            .Select((other, index) => other with { Id = new OtherId(firstId + index) })
            .ToImmutableArray();

    /// <summary>The links between two target objects; links to other target-plugin records are dropped.</summary>
    private ImmutableArray<TargetLink> ResolveLinks(IReadOnlyList<FoundTarget> sortedTargets)
    {
        var idByKey = Enumerable.Range(0, sortedTargets.Count).ToDictionary(index => sortedTargets[index].Target.Key, index => new TargetId(index));
        return
        [
            .. _links
                .Where(link => idByKey.ContainsKey(link.Source) && idByKey.ContainsKey(link.Linked))
                .Select(link => new TargetLink(idByKey[link.Source], idByKey[link.Linked])),
        ];
    }

    /// <summary>
    /// Comparison space of a cell: its worldspace for exterior cells (including the worldspace's
    /// persistent TopCell, whose context parent is the worldspace), otherwise the interior cell itself.
    /// </summary>
    private readonly record struct CellSpace(FormKey SpaceKey, IMajorRecordGetter SpaceRecord);

    private static CellSpace GetCellSpace(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        for (var current = cellContext.Parent; current != null; current = current.Parent)
        {
            if (current.Record is IWorldspaceGetter worldspace) return new CellSpace(worldspace.FormKey, worldspace);
        }
        return new CellSpace(cellContext.Record.FormKey, cellContext.Record);
    }
}
