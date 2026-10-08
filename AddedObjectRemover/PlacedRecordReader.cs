using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>What reading the placed records produced: the facts, and the load-order records behind them that later reads and writes need.</summary>
internal sealed record ScannedRecords(PlacedRecordFacts Facts, RecordHandles Handles, LandscapeTerrain Terrain, NavmeshRecords Navmeshes);

/// <summary>
/// The spaces (worldspace or interior cell keys) that contain a record created by the target
/// plugin, and every placed record from another plugin that the target plugin overrides.
/// </summary>
internal sealed record TargetPluginFootprint(HashSet<RecordKey> SpaceKeys, HashSet<RecordKey> OverriddenRecords);

/// <summary>
/// Reads every winning placed record (REFR, ACHR and all placed trap/hazard/projectile types) of a
/// load order as <see cref="PlacedRecordFact"/>s, with the links from them into the target plugin, the terrain
/// of the target worldspaces and the navmeshes of the target spaces.
///
/// Walks each mod's cell tree once via <c>EnumerateMajorRecordContexts&lt;ICell&gt;</c> (enumerating
/// IPlaced walks each tree three times) and reads each cell's Persistent and Temporary lists itself.
/// Mods are visited highest priority first, so the first sighting of a FormKey (record or cell)
/// is its winner.
/// </summary>
internal sealed class PlacedRecordReader
{
    private readonly ILoadOrder<IModListing<ISkyrimModGetter>> _loadOrder;
    private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;
    private readonly ModKey _patch;
    private readonly PluginNames _names;
    private readonly PlacedReadScope _scope;
    private readonly TargetPluginFootprint _footprint;
    private readonly HashSet<RecordKey> _landWorldspaces;
    private readonly HashSet<RecordKey> _seenRecords = new();
    private readonly HashSet<RecordKey> _seenNavmeshes = new();
    private readonly Dictionary<RecordKey, IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter>> _winningCells = new();

    private readonly List<PlacedRecordFact> _records = [];
    private readonly Dictionary<RecordKey, PlacedRecordHandle> _handles = new();
    private readonly List<LinkFact> _links = [];
    private readonly Dictionary<RecordKey, RecordKey> _landWorldspaceOf = new();
    private readonly Dictionary<ExteriorCell, ILandscapeGetter> _landscapes = new();
    private readonly Dictionary<RecordKey, List<CellNavmesh>> _navmeshes = new();

    private int _recordsScanned;

    private PlacedRecordReader(
        ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ModKey patch,
        PluginNames names,
        PlacedReadScope scope)
    {
        _loadOrder = loadOrder;
        _linkCache = linkCache;
        _patch = patch;
        _names = names;
        _scope = scope;
        _footprint = AnalyzeTargetPlugin();
        if (scope.Terrain) FindLandWorldspaces();
        _landWorldspaces = _landWorldspaceOf.Values.ToHashSet();
    }

    public static ScannedRecords Read(
        ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ModKey patch,
        PluginNames names,
        PlacedReadScope scope)
    {
        var reader = new PlacedRecordReader(loadOrder, linkCache, patch, names, scope);
        reader.ScanLoadOrder();
        return reader.CreateScannedRecords();
    }

    /// <summary>A plugin can only link to a target FormKey if it is the target, has it as a master, or is the patch.</summary>
    public static bool MayReferenceTarget(ISkyrimModGetter mod, PluginName target, ModKey patch, PluginNames names) =>
        names.Of(mod.ModKey) == target
        || mod.ModKey == patch
        || mod.MasterReferences.Any(master => names.Of(master.Master) == target);

    private void FindLandWorldspaces()
    {
        foreach (var spaceKey in _footprint.SpaceKeys)
        {
            if (!_linkCache.TryResolve<IWorldspaceGetter>(spaceKey.ToFormKey(), out var worldspace)) continue;
            _landWorldspaceOf[spaceKey] = FollowLandDataParents(worldspace);
        }
    }

    /// <remarks>A parent chain that loops back (broken data) stops at the last worldspace not yet seen.</remarks>
    private RecordKey FollowLandDataParents(IWorldspaceGetter worldspace)
    {
        var seen = new HashSet<FormKey> { worldspace.FormKey };
        while (worldspace.Parent is { } parent
               && parent.Flags.HasFlag(WorldspaceParent.Flag.UseLandData)
               && _linkCache.TryResolve<IWorldspaceGetter>(parent.Worldspace.FormKey, out var parentWorldspace)
               && seen.Add(parentWorldspace.FormKey))
        {
            worldspace = parentWorldspace;
        }
        return _names.KeyOf(worldspace.FormKey);
    }

    private void ScanLoadOrder()
    {
        foreach (var listing in _loadOrder.PriorityOrder)
        {
            if (listing.Mod is not { } mod) continue;
            var collectReferences = MayReferenceTarget(mod, _scope.Target, _patch, _names);
            var winningMod = _names.Of(listing.ModKey);
            foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_linkCache))
            {
                ScanCell(cellContext, winningMod, collectReferences);
            }
        }
    }

    /// <summary>
    /// Walks only the target plugin's own cell tree. A FormKey the target overrides is a
    /// replacement, never an "other mod" object, whichever plugin wins it.
    /// </summary>
    private TargetPluginFootprint AnalyzeTargetPlugin()
    {
        var footprint = new TargetPluginFootprint([], []);
        if (_loadOrder.ListedOrder.FirstOrDefault(listing => _names.Of(listing.ModKey) == _scope.Target)?.Mod is not { } mod) return footprint;

        foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_linkCache))
        {
            var hasTargetRecord = false;
            foreach (var (record, _) in cellContext.Record.EnumeratePlaced())
            {
                var recordKey = _names.KeyOf(record.FormKey);
                if (recordKey.Plugin == _scope.Target) hasTargetRecord = true;
                else footprint.OverriddenRecords.Add(recordKey);
            }
            if (hasTargetRecord) footprint.SpaceKeys.Add(GetCellSpace(cellContext).Key);
        }
        return footprint;
    }

    private void ScanCell(
        IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext,
        PluginName winningMod,
        bool collectReferences)
    {
        var cell = cellContext.Record;
        var space = GetCellSpace(cellContext);
        var cellFact = new CellFact(_names.KeyOf(cell.FormKey), cell.EditorID);
        var inTargetSpace = _footprint.SpaceKeys.Contains(space.Key);
        if (inTargetSpace) _winningCells.TryAdd(cellFact.Key, cellContext);
        if (_landWorldspaces.Contains(space.Key)) CollectLandscape(cell, cellFact.Key, space);
        if (_scope.Navmeshes && inTargetSpace) CollectNavmeshes(cell, cellFact.Key, space);

        foreach (var (record, persistent) in cell.EnumeratePlaced())
        {
            ScanRecord(record, persistent, new ScannedCell(space, cellFact, inTargetSpace, winningMod, collectReferences));
        }
    }

    /// <remarks>
    /// LAND is a record of its own inside the cell, so a cell override without it leaves the
    /// terrain to a lower-priority plugin; the first cell copy found that carries one is the winner.
    /// </remarks>
    private void CollectLandscape(ICellGetter cell, RecordKey cellKey, SpaceFact space)
    {
        if (space.Key == cellKey || cell.Grid is not { } grid || cell.Landscape is not { } landscape) return;
        _landscapes.TryAdd(new ExteriorCell(space.Key, grid.Point.X, grid.Point.Y), landscape);
    }

    /// <remarks>Like placed records, the first copy of a navmesh found is its winner.</remarks>
    private void CollectNavmeshes(ICellGetter cell, RecordKey cellKey, SpaceFact space)
    {
        (int X, int Y)? grid = space.Key != cellKey && cell.Grid is { } cellGrid ? (cellGrid.Point.X, cellGrid.Point.Y) : null;
        foreach (var navmesh in cell.NavigationMeshes)
        {
            if (!_seenNavmeshes.Add(_names.KeyOf(navmesh.FormKey)) || navmesh.IsDeleted || navmesh.Data is not { } data) continue;
            KeyedGroups.GetOrAddList(_navmeshes, space.Key).Add(new CellNavmesh(grid, data));
        }
    }

    private void ScanRecord(IPlacedGetter record, bool persistent, ScannedCell cell)
    {
        var key = _names.KeyOf(record.FormKey);
        if (!_seenRecords.Add(key)) return;
        _recordsScanned++;
        if (record.IsDeleted) return;

        if (cell.CollectReferences) LinkReader.ReadFromPlaced(record, key, _scope.Target, _names, _links);
        if (!cell.InTargetSpace && key.Plugin != _scope.Target && !_footprint.OverriddenRecords.Contains(key)) return;

        _records.Add(CreateFact(record, key, cell));
        if (cell.InTargetSpace && key.Plugin == _scope.Target)
        {
            _handles.Add(key, new PlacedRecordHandle(record, new PlacedRecordLocation(_winningCells[cell.Cell.Key], persistent)));
        }
    }

    private static PlacedRecordFact CreateFact(IPlacedGetter record, RecordKey key, ScannedCell cell) => new(
        key,
        record.EditorID,
        cell.WinningMod,
        cell.Space,
        cell.Cell,
        cell.InTargetSpace,
        new PlacementFacts(
            record.IsInitiallyDisabled(),
            record.EnableParent != null,
            record.Placement?.GetPosition(),
            record.Placement?.GetRotation()),
        record.Scale,
        record.GetBaseKey(),
        record is IPlacedObjectGetter { TeleportDestination: not null },
        record is IPlacedObjectGetter { Primitive: not null },
        record is IPlacedObjectGetter { MapMarker: not null },
        record.GetReferenceRadius(),
        record.GetPrimitiveBounds());

    private ScannedRecords CreateScannedRecords()
    {
        var terrain = new LandscapeTerrain(_landscapes, _landWorldspaceOf);
        var facts = new PlacedRecordFacts(
            [.. _records],
            _recordsScanned,
            _footprint.OverriddenRecords,
            [.. _links],
            _navmeshes.Values.Sum(navmeshes => navmeshes.Count));
        return new ScannedRecords(facts, new RecordHandles(_handles), terrain, new NavmeshRecords(_navmeshes));
    }

    /// <summary>What every record of one cell shares.</summary>
    private sealed record ScannedCell(SpaceFact Space, CellFact Cell, bool InTargetSpace, PluginName WinningMod, bool CollectReferences);

    /// <summary>
    /// Comparison space of a cell: its worldspace for exterior cells (including the worldspace's
    /// persistent TopCell, whose context parent is the worldspace), otherwise the interior cell itself.
    /// </summary>
    private SpaceFact GetCellSpace(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        for (var current = cellContext.Parent; current != null; current = current.Parent)
        {
            if (current.Record is IWorldspaceGetter worldspace)
            {
                return new SpaceFact(_names.KeyOf(worldspace.FormKey), worldspace.EditorID, SpaceKind.Worldspace);
            }
        }
        return new SpaceFact(_names.KeyOf(cellContext.Record.FormKey), cellContext.Record.EditorID, SpaceKind.Interior);
    }
}
