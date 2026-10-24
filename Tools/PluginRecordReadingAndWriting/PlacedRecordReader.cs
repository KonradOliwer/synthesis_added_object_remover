using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>What reading the placed records produced: the facts, and the load-order records behind them that later reads and writes need.</summary>
internal sealed record ScannedRecords(PlacedRecordFacts Facts, ScanLookups Lookups);

/// <summary>The load-order records behind the facts that later reads and writes need; kept without the facts, which only the caller holds.</summary>
internal sealed record ScanLookups(RecordHandles Handles, LandscapeTerrain Terrain, CellWater Water, NavmeshRecords Navmeshes);

/// <summary>
/// The spaces (worldspace or interior cell keys) where the target plugin places a record, one it created or one it overrides,
/// and every placed record from another plugin that the target plugin overrides.
/// </summary>
internal sealed record TargetFootprint(HashSet<RecordKey> SpaceKeys, HashSet<RecordKey> OverriddenRecords);

/// <summary>
/// Reads every winning placed record (REFR, ACHR and all placed trap/hazard/projectile types) of a
/// load order as <see cref="PlacedRecordFact"/>s, with the links from them into the target plugin, the terrain
/// and water of the target worldspaces and the navmeshes of the target spaces.
///
/// Walks each mod's cell tree once via <c>EnumerateMajorRecordContexts&lt;ICell&gt;</c> (enumerating
/// IPlaced walks each tree three times) and reads each cell's Persistent and Temporary lists itself.
/// Mods are visited highest priority first, so the first sighting of a FormKey (record or cell)
/// is its winner. The later sightings of a record that became a fact are its lower versions; they are kept down to and
/// including the first one whose plugin stops the walk.
/// </summary>
internal sealed class PlacedRecordReader
{
    private readonly ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> _loadOrder;
    private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;
    private readonly ModKey _patch;
    private readonly PluginNames _names;
    private readonly PlacedReadScope _scope;
    private readonly Func<PluginName, bool> _stopsTheWalk;
    private readonly TargetFootprint _footprint;
    private readonly HashSet<PluginName> _targetPlugin;
    private readonly HashSet<RecordKey> _landWorldspaces;
    private readonly HashSet<RecordKey> _seenRecords = new();
    private readonly HashSet<RecordKey> _seenNavmeshes = new();
    private readonly Dictionary<RecordKey, ICellGetter> _winningCells = new();
    private readonly Dictionary<RecordKey, SpaceFact> _spaces = new();
    private readonly List<SpaceCell> _targetSpaceCells = [];

    /// <summary>The facts in the order found; their versions are added when the walk is over.</summary>
    private readonly List<PlacedRecordFact> _records = [];

    /// <summary>Each fact's versions, highest first.</summary>
    private readonly Dictionary<RecordKey, List<PlacedVersionFact>> _versions = new();

    /// <summary>
    /// The version lists no plugin has stopped yet. Memory stays bounded: only records that became facts keep versions, and each
    /// keeps them only down to the first version whose plugin stops the walk.
    /// </summary>
    private readonly Dictionary<RecordKey, List<PlacedVersionFact>> _openVersions = new();
    private readonly Dictionary<RecordKey, PlacedRecordHandle> _handles = new();
    private readonly List<LinkFact> _links = [];
    private readonly Dictionary<RecordKey, RecordKey> _landWorldspaceOf = new();
    private readonly Dictionary<RecordKey, RecordKey> _waterWorldspaceOf = new();
    private readonly Dictionary<ExteriorCell, ILandscapeGetter> _landscapes = new();
    private readonly Dictionary<ExteriorCell, List<LandscapeVersion>> _landscapeVersions = new();
    private readonly Dictionary<ExteriorCell, List<CellWaterCopy>> _cellWaterCopies = new();
    private readonly Dictionary<RecordKey, List<CellNavmesh>> _navmeshes = new();

    private int _recordsScanned;

    private PlacedRecordReader(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ModKey patch,
        PluginNames names,
        PlacedReadScope scope,
        Func<PluginName, bool> stopsTheWalk)
    {
        _loadOrder = loadOrder;
        _linkCache = linkCache;
        _patch = patch;
        _names = names;
        _scope = scope;
        _stopsTheWalk = stopsTheWalk;
        _targetPlugin = [scope.Target];
        _footprint = AnalyzeTarget();
        if (scope.Terrain || scope.TerrainVersions || scope.Water) FindLandAndWaterWorldspaces();
        _landWorldspaces = _landWorldspaceOf.Values.ToHashSet();
    }

    public static ScannedRecords Read(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ModKey patch,
        PluginNames names,
        PlacedReadScope scope,
        Func<PluginName, bool> stopsTheWalk,
        Execution execution)
    {
        var reader = new PlacedRecordReader(loadOrder, linkCache, patch, names, scope, stopsTheWalk);
        reader.ScanLoadOrder();
        return reader.CreateScannedRecords(CellAreaReader.Read(reader._targetSpaceCells, new LocationNameReader(linkCache, scope.HoldKeyword.ToFormKey()), execution));
    }

    private void FindLandAndWaterWorldspaces()
    {
        foreach (var spaceKey in _footprint.SpaceKeys)
        {
            if (!_linkCache.TryResolve<IWorldspaceGetter>(spaceKey.ToFormKey(), out var worldspace)) continue;
            _landWorldspaceOf[spaceKey] = FollowParents(worldspace, WorldspaceParent.Flag.UseLandData);
            _waterWorldspaceOf[spaceKey] = FollowParents(worldspace, WorldspaceParent.Flag.UseWaterData);
        }
    }

    /// <remarks>
    /// A parent chain that loops back (broken data) stops at the last worldspace not yet seen. The parent and its flags come from
    /// the winning worldspace records only, so a mod switching "use water data" on a child is not seen as a water change; that
    /// misses the change but never causes a wrong removal.
    /// </remarks>
    private RecordKey FollowParents(IWorldspaceGetter worldspace, WorldspaceParent.Flag usesParent)
    {
        var seen = new HashSet<FormKey> { worldspace.FormKey };
        while (worldspace.Parent is { } parent
               && parent.Flags.HasFlag(usesParent)
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
            var collectReferences = LinkablePlugins.MayLinkToAny(mod, _targetPlugin, _patch, _names);
            var plugin = _names.Of(listing.ModKey);
            foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_linkCache))
            {
                ScanCell(cellContext, plugin, collectReferences);
            }
        }
    }

    /// <summary>Every plugin's own default water height of each worldspace that supplies water, in load order, when the water is asked for.</summary>
    private Dictionary<RecordKey, List<WorldspaceWaterCopy>> CollectWorldspaceWater()
    {
        var copies = new Dictionary<RecordKey, List<WorldspaceWaterCopy>>();
        if (!_scope.Water) return copies;
        var supplierKeys = _waterWorldspaceOf.Values.ToHashSet();
        foreach (var listing in _loadOrder.ListedOrder)
        {
            if (listing.Mod is not { } mod) continue;
            foreach (var worldspaceKey in supplierKeys)
            {
                if (mod.Worldspaces.RecordCache.TryGetValue(worldspaceKey.ToFormKey()) is not { } worldspace) continue;
                var copy = new WorldspaceWaterCopy(_names.Of(listing.ModKey), worldspace.LandDefaults?.DefaultWaterHeight);
                KeyedGroups.GetOrAddList(copies, worldspaceKey).Add(copy);
            }
        }
        return copies;
    }

    /// <summary>
    /// Walks only the target plugin's own cell tree, for the records of other plugins it overrides and the target spaces: every
    /// space where it places a record, its own or its override of another plugin's, which the caller can judge as a target object.
    /// A cell copy the target holds only to carry other data places nothing, so it adds no space.
    /// </summary>
    private TargetFootprint AnalyzeTarget()
    {
        var footprint = new TargetFootprint([], []);
        if (_loadOrder.ListedOrder.FirstOrDefault(listing => _names.Of(listing.ModKey) == _scope.Target)?.Mod is not { } mod) return footprint;

        foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_linkCache))
        {
            var placesARecord = false;
            foreach (var (record, _) in cellContext.Record.EnumeratePlaced())
            {
                var recordKey = _names.KeyOf(record.FormKey);
                if (recordKey.Plugin != _scope.Target) footprint.OverriddenRecords.Add(recordKey);
                placesARecord = true;
            }
            if (placesARecord) footprint.SpaceKeys.Add(GetCellSpaceKey(cellContext));
        }
        return footprint;
    }

    private void ScanCell(
        IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext,
        PluginName plugin,
        bool collectReferences)
    {
        var cell = cellContext.Record;
        var space = GetCellSpace(cellContext);
        var cellFact = new CellFact(_names.KeyOf(cell.FormKey), cell.EditorID);
        var inTargetSpace = _footprint.SpaceKeys.Contains(space.Key);
        if (inTargetSpace && _winningCells.TryAdd(cellFact.Key, cell)) _targetSpaceCells.Add(new SpaceCell(cellFact.Key, space, cell));
        if (ExteriorCellOf(cell, cellFact.Key, space) is { } exteriorCell)
        {
            if (_landWorldspaces.Contains(space.Key)) CollectLandscape(cell, exteriorCell, plugin);
            if (_scope.Water && _waterWorldspaceOf.ContainsKey(space.Key)) CollectCellWater(cell, exteriorCell, plugin);
        }
        if (_scope.Navmeshes && inTargetSpace) CollectNavmeshes(cell, cellFact.Key, space);

        foreach (var (record, persistent) in cell.EnumeratePlaced())
        {
            ScanRecord(record, persistent, new ScannedCell(space, cellFact, inTargetSpace, plugin, collectReferences));
        }
    }

    /// <remarks>
    /// LAND is a record of its own inside the cell, so a cell override without it leaves the
    /// terrain to a lower-priority plugin; the first cell copy found that carries one is the winner.
    /// With the terrain versions asked for, each plugin's copy is also kept, undecoded, in the order found.
    /// </remarks>
    private void CollectLandscape(ICellGetter cell, ExteriorCell exteriorCell, PluginName plugin)
    {
        if (cell.Landscape is not { } landscape) return;
        _landscapes.TryAdd(exteriorCell, landscape);
        if (_scope.TerrainVersions) KeyedGroups.GetOrAddList(_landscapeVersions, exteriorCell).Add(new LandscapeVersion(plugin, landscape));
    }

    /// <remarks>
    /// The water fields are part of the cell record itself, so they are read from the cells of the target's own worldspaces, not
    /// of the worldspace whose land they use. Every plugin's copy of the cell is kept, with or without land, in the order found;
    /// like the land, two cells at one grid in one plugin are both kept.
    /// </remarks>
    private void CollectCellWater(ICellGetter cell, ExteriorCell exteriorCell, PluginName plugin) =>
        KeyedGroups.GetOrAddList(_cellWaterCopies, exteriorCell).Add(WaterLevel.ReadCopy(plugin, cell));

    /// <returns>Null for a worldspace's persistent cell and any other cell without a grid.</returns>
    private static ExteriorCell? ExteriorCellOf(ICellGetter cell, RecordKey cellKey, SpaceFact space) =>
        space.Key != cellKey && cell.Grid is { } grid ? new ExteriorCell(space.Key, grid.Point.X, grid.Point.Y) : null;

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
        if (!_seenRecords.Add(key))
        {
            KeepLowerVersion(record, key, cell);
            return;
        }
        _recordsScanned++;

        var linksIntoTarget = ReadLinks(record, key, cell);
        if (!linksIntoTarget && !cell.InTargetSpace && key.Plugin != _scope.Target && !_footprint.OverriddenRecords.Contains(key)) return;

        var winner = ReadVersion(record, cell);
        _records.Add(CreateFact(record, key, cell, winner));
        StartVersions(key, winner);
        if (cell.InTargetSpace && (key.Plugin == _scope.Target || _footprint.OverriddenRecords.Contains(key)))
        {
            _handles.Add(key, new PlacedRecordHandle(record, new PlacedRecordLocation(_winningCells[cell.Cell.Key], persistent)));
        }
    }

    private void StartVersions(RecordKey key, PlacedVersionFact winner)
    {
        var versions = new List<PlacedVersionFact> { winner };
        _versions.Add(key, versions);
        if (!_stopsTheWalk(winner.Plugin)) _openVersions.Add(key, versions);
    }

    /// <remarks>A plugin that holds the record twice (broken data) gives one version, the first found, as it does for the winner.</remarks>
    private void KeepLowerVersion(IPlacedGetter record, RecordKey key, ScannedCell cell)
    {
        if (!_openVersions.TryGetValue(key, out var versions) || versions[^1].Plugin == cell.Plugin) return;
        versions.Add(ReadVersion(record, cell));
        if (_stopsTheWalk(cell.Plugin)) _openVersions.Remove(key);
    }

    /// <returns>True when a link was read; outside the target spaces only links into the target plugin are read.</returns>
    private bool ReadLinks(IPlacedGetter record, RecordKey key, ScannedCell cell)
    {
        if (!cell.CollectReferences && !cell.InTargetSpace) return false;
        var linksBefore = _links.Count;
        LinkReader.ReadFromPlaced(record, key, linked => IsLinkRead(linked, cell), _names, _links);
        return _links.Count > linksBefore;
    }

    /// <summary>
    /// A plugin that can link to the target plugin is read for its links into it. In the target spaces every plugin is also read
    /// for its links into the records the target plugin overrides: the caller can judge such a record as a target object.
    /// </summary>
    private bool IsLinkRead(RecordKey linked, ScannedCell cell) =>
        (cell.CollectReferences && linked.Plugin == _scope.Target) || (cell.InTargetSpace && _footprint.OverriddenRecords.Contains(linked));

    /// <param name="winner">The record's winning version, read from <paramref name="record"/>; the lower versions are added when the walk ends.</param>
    private PlacedRecordFact CreateFact(IPlacedGetter record, RecordKey key, ScannedCell cell, PlacedVersionFact winner) => new(
        key,
        record.EditorID,
        cell.Space,
        cell.Cell,
        cell.InTargetSpace,
        record is IPlacedObjectGetter { TeleportDestination: not null },
        record is IPlacedObjectGetter { Primitive: not null },
        record is IPlacedObjectGetter { MapMarker: not null },
        record.IsPersistent(),
        ScriptPropertyReader.FindQuestOrScene(record.VirtualMachineAdapter, _linkCache, _names),
        record.LocationRefTypes is { Count: > 0 },
        record.GetReferenceRadius(),
        record.GetPrimitiveBounds(),
        Versions: [winner]);

    private PlacedVersionFact ReadVersion(IPlacedGetter record, ScannedCell cell) => new(
        cell.Plugin,
        new PlacementFacts(ReadPresence(record), record.Placement?.GetPosition(), record.Placement?.GetRotation()),
        record.Scale,
        record.GetBaseKey(_names));

    private PresenceFacts ReadPresence(IPlacedGetter record) => new(
        record.IsDeleted,
        record.IsInitiallyDisabled(),
        record.GetEnableParent() is { } parent
            ? new EnableParentFact(_names.KeyOf(parent.Reference.FormKey), parent.Flags.HasFlag(EnableParent.Flag.SetEnableStateToOppositeOfParent))
            : null);

    private ScannedRecords CreateScannedRecords(IReadOnlyDictionary<RecordKey, CellAreaFact> cellAreas)
    {
        var terrain = new LandscapeTerrain(_landscapes, _landWorldspaceOf, _scope.TerrainVersions ? _landscapeVersions : null, _scope.TerrainLayers ? _names : null);
        var facts = new PlacedRecordFacts(
            [.. _records.Select(WithVersions)],
            _recordsScanned,
            _footprint.OverriddenRecords,
            [.. _links],
            _navmeshes.Values.Sum(navmeshes => navmeshes.Count),
            cellAreas);
        var water = new CellWater(_scope.Water ? _cellWaterCopies : null, CollectWorldspaceWater(), _waterWorldspaceOf);
        return new ScannedRecords(facts, new ScanLookups(new RecordHandles(_handles), terrain, water, new NavmeshRecords(_navmeshes)));
    }

    private PlacedRecordFact WithVersions(PlacedRecordFact fact) => fact with { Versions = [.. Enumerable.Reverse(_versions[fact.Key])] };

    /// <summary>What every record of one cell shares.</summary>
    /// <param name="Plugin">The plugin whose copy of the cell is walked.</param>
    private sealed record ScannedCell(SpaceFact Space, CellFact Cell, bool InTargetSpace, PluginName Plugin, bool CollectReferences);

    /// <summary>
    /// Comparison space of a cell: its worldspace for exterior cells (including the worldspace's
    /// persistent TopCell, whose context parent is the worldspace), otherwise the interior cell itself.
    /// </summary>
    private RecordKey GetCellSpaceKey(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext) =>
        _names.KeyOf(FindWorldspace(cellContext)?.FormKey ?? cellContext.Record.FormKey);

    /// <remarks>Read at the space's first sighting, which for an interior is its winning cell.</remarks>
    private SpaceFact GetCellSpace(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        var key = GetCellSpaceKey(cellContext);
        if (_spaces.TryGetValue(key, out var space)) return space;
        space = ReadSpace(key, cellContext);
        _spaces.Add(key, space);
        return space;
    }

    private SpaceFact ReadSpace(RecordKey key, IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        if (FindWorldspace(cellContext) is not { } found)
        {
            var cell = cellContext.Record;
            return new SpaceFact(key, cell.EditorID, SpaceKind.Interior, RecordDisplayNames.Of(cell.Name, cell.EditorID));
        }
        var worldspace = _linkCache.Resolve<IWorldspaceGetter>(found.FormKey);
        return new SpaceFact(key, worldspace.EditorID, SpaceKind.Worldspace, RecordDisplayNames.Of(worldspace.Name, worldspace.EditorID));
    }

    private static IWorldspaceGetter? FindWorldspace(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        for (var current = cellContext.Parent; current != null; current = current.Parent)
        {
            if (current.Record is IWorldspaceGetter worldspace) return worldspace;
        }
        return null;
    }
}
