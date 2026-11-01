using System.Collections.Immutable;
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

/// <summary>What every record of one cell shares.</summary>
/// <param name="Plugin">The plugin whose copy of the cell is walked.</param>
internal sealed record ScannedCell(SpaceFact Space, CellFact Cell, bool InTargetSpace, PluginName Plugin, bool CollectReferences);

/// <summary>
/// Reads every winning placed record (REFR, ACHR and all placed trap/hazard/projectile types) of a
/// load order as <see cref="PlacedRecordFact"/>s, with the links from them into the read's plugin, the terrain
/// and water of the target worldspaces and the navmeshes of the target spaces.
///
/// Walks each mod's cell tree once via <c>EnumerateMajorRecordContexts&lt;ICell&gt;</c> (enumerating
/// IPlaced walks each tree three times) and reads each cell's Persistent and Temporary lists itself.
/// Mods are visited highest priority first, so the first sighting of a FormKey (record or cell)
/// is its winner. The later sightings of a record that became a fact are its lower versions (<see cref="PlacedVersionReader"/>).
/// The spaces and cells are read by <see cref="SpaceReader"/>, the LAND records and the water by <see cref="LandscapeReader"/>, and
/// the navmeshes of the target spaces the read's plugin overrides, deletes or adds by <see cref="NavmeshVersionCollector"/>.
/// </summary>
internal sealed class PlacedRecordReader
{
    private readonly ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> _loadOrder;
    private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;
    private readonly ModKey _patch;
    private readonly PluginNames _names;
    private readonly PlacedReadScope _scope;
    private readonly PlacedRecordParts _parts;
    private readonly TargetFootprint _footprint;
    private readonly HashSet<PluginName> _targetPlugin;
    private readonly HashSet<RecordKey> _seenRecords = new();
    private readonly HashSet<RecordKey> _seenNavmeshes = new();
    private readonly SpaceReader _spaceReader;
    private readonly LandscapeReader _landscapeReader;
    private readonly PlacedVersionReader _versionReader;

    /// <summary>The facts in the order found; their versions are added when the walk is over.</summary>
    private readonly List<PlacedRecordFact> _records = [];

    private readonly Dictionary<RecordKey, PlacedRecordHandle> _handles = new();
    private readonly List<LinkFact> _links = [];
    private readonly HashSet<RecordKey> _linkedFromPlacedRecords = [];
    private readonly Dictionary<RecordKey, List<CellNavmesh>> _navmeshes = new();

    /// <summary>Null when the area record versions were not asked for.</summary>
    private readonly AreaRecordTables? _areaTables;

    /// <summary>
    /// Null when the area record versions were not asked for, or the read's plugin changes no navmesh of the target spaces and
    /// overrides no placed record of another plugin, whose navmesh link then needs no navmesh; then no navmesh info map or edge link
    /// is read.
    /// </summary>
    private readonly NavmeshVersionCollector? _navmeshVersions;

    private int _recordsScanned;

    /// <summary>The walk has passed the read's plugin: the plugin walked now loads before it.</summary>
    private bool _belowTarget;

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
        _parts = new PlacedRecordParts(scope.PartTolerances);
        _areaTables = scope.AreaRecordVersions is { Tolerances: var tolerances }
            ? new AreaRecordTables(new CellRecordParts(tolerances.WaterHeight), new LandRecordParts(tolerances.LandHeight))
            : null;
        _targetPlugin = [scope.Plugin];
        (_footprint, var overriddenLands) = new TargetFootprintReader(loadOrder, linkCache, names, scope).AnalyzeTarget();
        _navmeshVersions = _areaTables is not null && (_footprint.ChangesAnyNavmesh() || _footprint.OverriddenRecords.Count > 0)
            ? new NavmeshVersionCollector(scope.Plugin, _footprint, names)
            : null;
        _scope = DropTerrainReadsWithoutAnOverride(scope, _footprint);
        _spaceReader = new SpaceReader(linkCache, names, _footprint, scope.Plugin, _areaTables?.Cells);
        if (_scope.Terrain || _scope.TerrainVersions || _scope.Water || _areaTables is not null) _spaceReader.FindLandAndWaterWorldspaces();
        _landscapeReader = new LandscapeReader(
            loadOrder, names, _scope, _footprint, _spaceReader.LandWorldspaceOf, _spaceReader.WaterWorldspaceOf, _areaTables?.Lands);
        _footprint.OverriddenLands.UnionWith(overriddenLands.Where(land => _landscapeReader.LandWorldspaces.Contains(land.Space)).Select(land => land.Land));
        _versionReader = new PlacedVersionReader(scope.Plugin, names, _footprint, stopsTheWalk, _parts);
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
        return reader.CreateScannedRecords(
            CellAreaReader.Read(reader._spaceReader.TargetSpaceCells, new LocationNameReader(linkCache, scope.HoldKeyword.ToFormKey()), execution));
    }

    /// <remarks>The footprint is analyzed with the scope as given; this narrows it afterwards, since the footprint decides it.</remarks>
    private static PlacedReadScope DropTerrainReadsWithoutAnOverride(PlacedReadScope scope, TargetFootprint footprint) =>
        scope.TerrainReadsNeedAnOverride && footprint.OverriddenRecords.Count == 0
            ? scope with { TerrainVersions = false, TerrainLayers = false, Water = false }
            : scope;

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
            if (plugin == _scope.Plugin) _belowTarget = true;
        }
    }

    private void ScanCell(
        IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext,
        PluginName plugin,
        bool collectReferences)
    {
        var cell = cellContext.Record;
        var space = _spaceReader.GetCellSpace(cellContext);
        var cellFact = new CellFact(_names.KeyOf(cell.FormKey), cell.EditorID);
        var inTargetSpace = _footprint.SpaceKeys.Contains(space.Key);
        _spaceReader.SeeCell(cell, cellFact.Key, space, plugin, inTargetSpace);
        _landscapeReader.SeeCell(cell, cellFact.Key, space, plugin, _belowTarget);
        if (_scope.Navmeshes && inTargetSpace) CollectNavmeshes(cell, cellFact.Key, space);
        if (inTargetSpace) _navmeshVersions?.SeeNavmeshes(cell, cellFact.Key, space.Key, plugin, _belowTarget);

        foreach (var (record, persistent) in cell.EnumeratePlaced())
        {
            ScanRecord(record, persistent, new ScannedCell(space, cellFact, inTargetSpace, plugin, collectReferences));
        }
    }

    /// <remarks>Like placed records, the first copy of a navmesh found is its winner.</remarks>
    private void CollectNavmeshes(ICellGetter cell, RecordKey cellKey, SpaceFact space)
    {
        var grid = SpaceReader.GridOf(cell, cellKey, space);
        foreach (var navmesh in cell.NavigationMeshes)
        {
            var key = _names.KeyOf(navmesh.FormKey);
            if (!_seenNavmeshes.Add(key) || navmesh.IsDeleted || navmesh.Data is not { } data) continue;
            KeyedGroups.GetOrAddList(_navmeshes, space.Key).Add(new CellNavmesh(key, grid, data));
        }
    }

    private void ScanRecord(IPlacedGetter record, bool persistent, ScannedCell cell)
    {
        var key = _names.KeyOf(record.FormKey);
        var isWinner = _seenRecords.Add(key);
        if (cell.InTargetSpace) _navmeshVersions?.SeeDoorVersion(key, record, cell.Cell.Key, isWinner, _belowTarget);
        if (!isWinner)
        {
            _versionReader.KeepLowerVersion(record, key, cell);
            return;
        }
        _recordsScanned++;

        var linksIntoTarget = ReadLinks(record, key, cell);
        if (!linksIntoTarget && !cell.InTargetSpace && key.Plugin != _scope.Plugin && !_footprint.OverriddenRecords.Contains(key)) return;

        var winner = _versionReader.ReadVersion(record, cell);
        _records.Add(CreateFact(record, key, cell, winner));
        _versionReader.StartVersions(record, key, winner);
        if (!cell.InTargetSpace) return;

        _handles.Add(key, new PlacedRecordHandle(record, new PlacedRecordLocation(_spaceReader.WinningCellOf(cell.Cell.Key), persistent)));
    }

    /// <returns>True when a link was read; outside the target spaces only links into the read's plugin are read.</returns>
    private bool ReadLinks(IPlacedGetter record, RecordKey key, ScannedCell cell)
    {
        if (!cell.CollectReferences && !cell.InTargetSpace) return false;
        var linksBefore = _links.Count;
        LinkReader.ReadFromPlaced(record, key, linked => IsLinkRead(linked, cell), _names, _links, cell.InTargetSpace ? _linkedFromPlacedRecords : null);
        return _links.Count > linksBefore;
    }

    /// <summary>
    /// A plugin that can link to the read's plugin is read for its links into it. In the target spaces every plugin is also read
    /// for its links into the records the read's plugin overrides: the caller can judge such a record as a target object, and
    /// tells which placed records and cells of other plugins the read's plugin's records link to.
    /// </summary>
    private bool IsLinkRead(RecordKey linked, ScannedCell cell) =>
        (cell.CollectReferences && linked.Plugin == _scope.Plugin)
        || (cell.InTargetSpace && (_footprint.OverriddenRecords.Contains(linked) || _footprint.OverriddenCells.Contains(linked)));

    /// <param name="winner">The record's winning version, read from <paramref name="record"/>; the lower versions are added when the walk ends.</param>
    private PlacedRecordFact CreateFact(IPlacedGetter record, RecordKey key, ScannedCell cell, PlacedVersionFact winner) => new(
        key,
        record.EditorID,
        cell.Space,
        cell.Cell,
        cell.InTargetSpace,
        winner.Reference.IsTeleportDoor,
        winner.Reference.IsPrimitive,
        winner.Reference.HasMapMarker,
        record.IsPersistent(),
        ScriptPropertyReader.FindQuestOrScene(record.VirtualMachineAdapter, _linkCache, _names),
        record.LocationRefTypes is { Count: > 0 },
        winner.Reference.ReferenceRadius,
        winner.Reference.PrimitiveBounds,
        Versions: [winner],
        PartDifferences: null);

    private ScannedRecords CreateScannedRecords(IReadOnlyDictionary<RecordKey, CellAreaFact> cellAreas)
    {
        var terrain = _landscapeReader.CreateTerrain();
        var navmeshes = _navmeshVersions?.Compare(_loadOrder);
        var changedCells = _spaceReader.CompareCells();
        var changedLands = _landscapeReader.CompareLands();
        var facts = new PlacedRecordFacts(
            [.. _records.Select(_versionReader.WithVersions)],
            _recordsScanned,
            _footprint.OverriddenRecords,
            [.. _links],
            _navmeshes.Values.Sum(navmeshes => navmeshes.Count),
            cellAreas,
            _linkedFromPlacedRecords,
            changedCells,
            changedLands,
            navmeshes?.Facts ?? ImmutableArray<NavmeshRecordFact>.Empty,
            _spaceReader.DeletedCells,
            navmeshes?.OverriddenDoorLinks ?? new Dictionary<RecordKey, NavDoorLinkVersions>(),
            _spaceReader.CellsDeletedInFinalWorld,
            _spaceReader.Spaces);
        var water = _landscapeReader.CreateWater();
        var areaHandles = CreateAreaRecordHandles(changedCells, changedLands, navmeshes?.Handles ?? NavmeshRecordHandles.Empty);
        var handles = new RecordHandles(_handles, _versionReader.EarlierRecords, _parts, areaHandles);
        return new ScannedRecords(facts, new ScanLookups(handles, terrain, water, new NavmeshRecords(_navmeshes)));
    }

    /// <remarks>Only the records with a fact keep their versions: nothing is written into the others.</remarks>
    private AreaRecordHandles? CreateAreaRecordHandles(ImmutableArray<CellRecordFact> cells, ImmutableArray<LandRecordFact> lands, NavmeshRecordHandles navmeshes) =>
        _areaTables is null
            ? null
            : new AreaRecordHandles(
                _spaceReader.CellVersionsOf(cells.Select(cell => cell.Key)),
                _landscapeReader.LandVersionsOf(lands.Select(land => land.Key)),
                navmeshes,
                _areaTables.Cells,
                _areaTables.Lands);

    /// <summary>The part tables the cell and LAND records are compared with.</summary>
    private sealed record AreaRecordTables(CellRecordParts Cells, LandRecordParts Lands);
}
