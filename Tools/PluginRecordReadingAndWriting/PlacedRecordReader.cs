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

/// <summary>
/// The spaces (worldspace or interior cell keys) where the target plugin places a record, one it created or one it overrides,
/// and every placed record from another plugin that the target plugin overrides.
/// </summary>
/// <param name="OverriddenCells">
/// Only when the cell versions are asked for: the cell records of other plugins that the target plugin overrides in the target
/// spaces, the persistent cell of a worldspace left out.
/// </param>
/// <param name="OverriddenLands">
/// Only when the LAND versions are asked for: the LAND records of other plugins that the target plugin overrides in the worldspaces
/// whose land a target space uses.
/// </param>
/// <param name="OverriddenNavmeshes">
/// Only when the area record versions are asked for: the navmeshes of other plugins in the target spaces that the target plugin
/// overrides without deleting them.
/// </param>
/// <param name="DeletedNavmeshes">Likewise, the navmeshes of other plugins in the target spaces that the target plugin's version deletes.</param>
/// <param name="AddedNavmeshes">Likewise, the navmeshes the target plugin itself defines in the target spaces.</param>
internal sealed record TargetFootprint(
    HashSet<RecordKey> SpaceKeys,
    HashSet<RecordKey> OverriddenRecords,
    HashSet<RecordKey> OverriddenCells,
    HashSet<RecordKey> OverriddenLands,
    HashSet<RecordKey> OverriddenNavmeshes,
    HashSet<RecordKey> DeletedNavmeshes,
    HashSet<RecordKey> AddedNavmeshes)
{
    public bool ChangesNavmesh(RecordKey navmesh) =>
        OverriddenNavmeshes.Contains(navmesh) || DeletedNavmeshes.Contains(navmesh) || AddedNavmeshes.Contains(navmesh);

    public bool ChangesAnyNavmesh() => OverriddenNavmeshes.Count > 0 || DeletedNavmeshes.Count > 0 || AddedNavmeshes.Count > 0;
}

/// <summary>
/// Reads every winning placed record (REFR, ACHR and all placed trap/hazard/projectile types) of a
/// load order as <see cref="PlacedRecordFact"/>s, with the links from them into the target plugin, the terrain
/// and water of the target worldspaces and the navmeshes of the target spaces.
///
/// Walks each mod's cell tree once via <c>EnumerateMajorRecordContexts&lt;ICell&gt;</c> (enumerating
/// IPlaced walks each tree three times) and reads each cell's Persistent and Temporary lists itself.
/// Mods are visited highest priority first, so the first sighting of a FormKey (record or cell)
/// is its winner. The later sightings of a record that became a fact are its lower versions; they are kept down to and
/// including the first one whose plugin stops the walk. For a record the target plugin overrides, no plugin stops the walk before
/// the version just below the target plugin's is kept, and the parts of the versions from that one up are compared
/// (<see cref="PlacedPartHistory"/>). The cell and LAND records of other plugins the target plugin overrides are walked the same
/// way when asked for, and compared (<see cref="CellPartHistory"/>, <see cref="LandPartHistory"/>), together with the navmeshes of the
/// target spaces it overrides, deletes or adds (<see cref="NavmeshVersionCollector"/>).
/// </summary>
internal sealed class PlacedRecordReader
{
    private readonly ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> _loadOrder;
    private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;
    private readonly ModKey _patch;
    private readonly PluginNames _names;
    private readonly PlacedReadScope _scope;
    private readonly Func<PluginName, bool> _stopsTheWalk;
    private readonly PlacedRecordParts _parts;
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

    /// <summary>For each record the target plugin overrides: the version just below the target plugin's, as its plugin stores it.</summary>
    private readonly Dictionary<RecordKey, IPlacedGetter> _earlierRecords = new();

    /// <summary>
    /// The versions, highest first, of each record the target plugin overrides whose version just below the target plugin's is not
    /// read yet. Memory stays bounded: each list is dropped once that version is read and the parts are compared.
    /// </summary>
    private readonly Dictionary<RecordKey, List<IPlacedGetter>> _versionsToCompare = new();
    private readonly Dictionary<RecordKey, PlacedPartDifferences> _partDifferences = new();
    private readonly List<LinkFact> _links = [];
    private readonly HashSet<RecordKey> _linkedFromPlacedRecords = [];
    private readonly Dictionary<RecordKey, RecordKey> _landWorldspaceOf = new();
    private readonly Dictionary<RecordKey, RecordKey> _waterWorldspaceOf = new();
    private readonly Dictionary<ExteriorCell, ILandscapeGetter> _landscapes = new();
    private readonly Dictionary<ExteriorCell, ILandscapeGetter> _landscapesBelowTarget = new();
    private readonly Dictionary<ExteriorCell, List<LandscapeVersion>> _landscapeVersions = new();
    private readonly Dictionary<ExteriorCell, List<CellWaterCopy>> _cellWaterCopies = new();
    private readonly Dictionary<RecordKey, List<CellNavmesh>> _navmeshes = new();

    /// <summary>With the area record versions asked for, the cells of the target spaces that some plugin's version deletes.</summary>
    private readonly HashSet<RecordKey> _deletedCells = new();

    /// <summary>The cells of the target spaces whose winning version deletes them, with that version's plugin.</summary>
    private readonly Dictionary<RecordKey, PluginName> _cellsDeletedInFinalWorld = new();

    /// <summary>With the area record versions asked for, the cells of the land worldspaces that some plugin's version deletes.</summary>
    private readonly HashSet<ExteriorCell> _deletedLandCells = new();

    /// <summary>Null when the area record versions were not asked for.</summary>
    private readonly AreaRecordTables? _areaTables;

    private readonly TargetOverrideWalk<ICellGetter, CellPlace> _cellWalk;
    private readonly TargetOverrideWalk<ILandscapeGetter, ExteriorCell> _landWalk;

    /// <summary>
    /// Null when the area record versions were not asked for, or the target plugin changes no navmesh of the target spaces and
    /// overrides no placed record of another plugin, whose navmesh link then needs no navmesh; then no navmesh info map or edge link
    /// is read.
    /// </summary>
    private readonly NavmeshVersionCollector? _navmeshVersions;

    private int _recordsScanned;

    /// <summary>The walk has passed the target plugin: the plugin walked now loads before it.</summary>
    private bool _belowTarget;

    /// <summary>The LAND area record versions are asked for.</summary>
    private bool ReadsLandVersions => _scope.AreaRecordVersions is { Lands: true };

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
        _parts = new PlacedRecordParts(scope.PartTolerances);
        _areaTables = scope.AreaRecordVersions is { Tolerances: var tolerances }
            ? new AreaRecordTables(new CellRecordParts(tolerances.WaterHeight), new LandRecordParts(tolerances.LandHeight))
            : null;
        _cellWalk = new(scope.Target);
        _landWalk = new(scope.Target);
        _targetPlugin = [scope.Target];
        (_footprint, var overriddenLands) = AnalyzeTarget();
        _navmeshVersions = _areaTables is not null && (_footprint.ChangesAnyNavmesh() || _footprint.OverriddenRecords.Count > 0)
            ? new NavmeshVersionCollector(scope.Target, _footprint, names)
            : null;
        _scope = DropTerrainReadsWithoutAnOverride(scope, _footprint);
        if (_scope.Terrain || _scope.TerrainVersions || _scope.Water || _areaTables is not null) FindLandAndWaterWorldspaces();
        _landWorldspaces = _landWorldspaceOf.Values.ToHashSet();
        _footprint.OverriddenLands.UnionWith(overriddenLands.Where(land => _landWorldspaces.Contains(land.Space)).Select(land => land.Land));
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

    /// <remarks>The footprint is analyzed with the scope as given; this narrows it afterwards, since the footprint decides it.</remarks>
    private static PlacedReadScope DropTerrainReadsWithoutAnOverride(PlacedReadScope scope, TargetFootprint footprint) =>
        scope.TerrainReadsNeedAnOverride && footprint.OverriddenRecords.Count == 0
            ? scope with { TerrainVersions = false, TerrainLayers = false, Water = false }
            : scope;

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
            if (plugin == _scope.Target) _belowTarget = true;
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
    /// With the area record versions asked for, it also finds the cell and LAND records of other plugins it overrides, as far as
    /// asked for, and the navmeshes it overrides, deletes or adds.
    /// </summary>
    /// <returns>
    /// The footprint, its overridden cells already narrowed to the target spaces; and every overridden LAND record with the space of
    /// its cell, which the caller narrows once it knows the worldspaces whose land the target spaces use.
    /// </returns>
    private (TargetFootprint Footprint, List<(RecordKey Land, RecordKey Space)> OverriddenLands) AnalyzeTarget()
    {
        var footprint = new TargetFootprint([], [], [], [], [], [], []);
        var overriddenCells = new List<(RecordKey Cell, RecordKey Space)>();
        var overriddenLands = new List<(RecordKey Land, RecordKey Space)>();
        var navmeshes = new List<(INavigationMeshGetter Navmesh, RecordKey Key, RecordKey Space)>();
        if (_loadOrder.ListedOrder.FirstOrDefault(listing => _names.Of(listing.ModKey) == _scope.Target)?.Mod is not { } mod) return (footprint, overriddenLands);

        foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_linkCache))
        {
            var placesARecord = false;
            foreach (var (record, _) in cellContext.Record.EnumeratePlaced())
            {
                var recordKey = _names.KeyOf(record.FormKey);
                if (recordKey.Plugin != _scope.Target) footprint.OverriddenRecords.Add(recordKey);
                placesARecord = true;
            }
            var spaceKey = GetCellSpaceKey(cellContext);
            if (placesARecord) footprint.SpaceKeys.Add(spaceKey);
            if (_scope.AreaRecordVersions is not { } areaReads) continue;
            FindOverriddenCellAndLand(cellContext.Record, spaceKey, areaReads, overriddenCells, overriddenLands);
            navmeshes.AddRange(cellContext.Record.NavigationMeshes.Select(navmesh => (navmesh, _names.KeyOf(navmesh.FormKey), spaceKey)));
        }
        footprint.OverriddenCells.UnionWith(overriddenCells.Where(cell => footprint.SpaceKeys.Contains(cell.Space)).Select(cell => cell.Cell));
        ClassifyChangedNavmeshes(navmeshes.Where(navmesh => footprint.SpaceKeys.Contains(navmesh.Space)), footprint);
        return (footprint, overriddenLands);
    }

    private void ClassifyChangedNavmeshes(IEnumerable<(INavigationMeshGetter Navmesh, RecordKey Key, RecordKey Space)> navmeshes, TargetFootprint footprint)
    {
        foreach (var (navmesh, key, _) in navmeshes)
        {
            var kind = key.Plugin == _scope.Target ? footprint.AddedNavmeshes : navmesh.IsDeleted ? footprint.DeletedNavmeshes : footprint.OverriddenNavmeshes;
            kind.Add(key);
        }
    }

    /// <remarks>The persistent cell of a worldspace holds fields that act worldspace-wide, so it is no cell of an area.</remarks>
    private void FindOverriddenCellAndLand(
        ICellGetter cell,
        RecordKey spaceKey,
        AreaRecordReads areaReads,
        List<(RecordKey Cell, RecordKey Space)> overriddenCells,
        List<(RecordKey Land, RecordKey Space)> overriddenLands)
    {
        var cellKey = _names.KeyOf(cell.FormKey);
        if (areaReads.Cells && cellKey.Plugin != _scope.Target && (spaceKey == cellKey || cell.Grid is not null)) overriddenCells.Add((cellKey, spaceKey));
        if (areaReads.Lands && cell.Landscape is { } landscape && _names.KeyOf(landscape.FormKey) is var landKey && landKey.Plugin != _scope.Target)
        {
            overriddenLands.Add((landKey, spaceKey));
        }
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
        if (inTargetSpace && _winningCells.TryAdd(cellFact.Key, cell)) KeepWinningCell(cell, cellFact.Key, space, plugin);
        if (_footprint.OverriddenCells.Contains(cellFact.Key)) _cellWalk.Keep(cellFact.Key, plugin, cell, new CellPlace(space.Key, GridOf(cell, cellFact.Key, space)));
        if (_areaTables is not null && inTargetSpace && cell.IsDeleted) _deletedCells.Add(cellFact.Key);
        if (ExteriorCellOf(cell, cellFact.Key, space) is { } exteriorCell)
        {
            if (_areaTables is not null && cell.IsDeleted && _landWorldspaces.Contains(space.Key)) _deletedLandCells.Add(exteriorCell);
            if (_landWorldspaces.Contains(space.Key)) CollectLandscape(cell, exteriorCell, plugin);
            if (_scope.Water && _waterWorldspaceOf.ContainsKey(space.Key)) CollectCellWater(cell, exteriorCell, plugin);
        }
        if (_scope.Navmeshes && inTargetSpace) CollectNavmeshes(cell, cellFact.Key, space);
        if (inTargetSpace) _navmeshVersions?.SeeNavmeshes(cell, cellFact.Key, space.Key, plugin, _belowTarget);

        foreach (var (record, persistent) in cell.EnumeratePlaced())
        {
            ScanRecord(record, persistent, new ScannedCell(space, cellFact, inTargetSpace, plugin, collectReferences));
        }
    }

    private void KeepWinningCell(ICellGetter cell, RecordKey cellKey, SpaceFact space, PluginName plugin)
    {
        _targetSpaceCells.Add(new SpaceCell(cellKey, space, cell));
        if (cell.IsDeleted) _cellsDeletedInFinalWorld.Add(cellKey, plugin);
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
        if (_belowTarget && ReadsLandVersions) _landscapesBelowTarget.TryAdd(exteriorCell, landscape);
        if (_scope.TerrainVersions) KeyedGroups.GetOrAddList(_landscapeVersions, exteriorCell).Add(new LandscapeVersion(plugin, landscape));
        if (_footprint.OverriddenLands.Count > 0 && _names.KeyOf(landscape.FormKey) is var landKey && _footprint.OverriddenLands.Contains(landKey))
        {
            _landWalk.Keep(landKey, plugin, landscape, exteriorCell);
        }
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

    /// <returns>Null for an interior and for a worldspace's persistent cell.</returns>
    private static (int X, int Y)? GridOf(ICellGetter cell, RecordKey cellKey, SpaceFact space) =>
        space.Key != cellKey && cell.Grid is { } grid ? (grid.Point.X, grid.Point.Y) : null;

    /// <remarks>Like placed records, the first copy of a navmesh found is its winner.</remarks>
    private void CollectNavmeshes(ICellGetter cell, RecordKey cellKey, SpaceFact space)
    {
        var grid = GridOf(cell, cellKey, space);
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
            KeepLowerVersion(record, key, cell);
            return;
        }
        _recordsScanned++;

        var linksIntoTarget = ReadLinks(record, key, cell);
        if (!linksIntoTarget && !cell.InTargetSpace && key.Plugin != _scope.Target && !_footprint.OverriddenRecords.Contains(key)) return;

        var winner = ReadVersion(record, cell);
        _records.Add(CreateFact(record, key, cell, winner));
        StartVersions(record, key, winner);
        if (!cell.InTargetSpace) return;

        _handles.Add(key, new PlacedRecordHandle(record, new PlacedRecordLocation(_winningCells[cell.Cell.Key], persistent)));
    }

    private void StartVersions(IPlacedGetter record, RecordKey key, PlacedVersionFact winner)
    {
        var versions = new List<PlacedVersionFact> { winner };
        _versions.Add(key, versions);
        if (_footprint.OverriddenRecords.Contains(key)) _versionsToCompare.Add(key, [record]);
        if (!StopsTheWalk(key, winner.Plugin)) _openVersions.Add(key, versions);
    }

    /// <remarks>A plugin that holds the record twice (broken data) gives one version, the first found, as it does for the winner.</remarks>
    private void KeepLowerVersion(IPlacedGetter record, RecordKey key, ScannedCell cell)
    {
        if (!_openVersions.TryGetValue(key, out var versions) || versions[^1].Plugin == cell.Plugin) return;
        var isBelowTarget = versions[^1].Plugin == _scope.Target;
        if (isBelowTarget && _footprint.OverriddenRecords.Contains(key)) _earlierRecords.Add(key, record);
        versions.Add(ReadVersion(record, cell));
        if (_versionsToCompare.TryGetValue(key, out var versionsToCompare)) KeepVersionToCompare(record, key, versionsToCompare, isBelowTarget, versions);
        if (StopsTheWalk(key, cell.Plugin)) _openVersions.Remove(key);
    }

    /// <summary>Once the version just below the target plugin's is read, compares the parts of the record's versions.</summary>
    /// <param name="versions">The versions read so far, highest first; <paramref name="versionsToCompare"/> holds the same versions as stored.</param>
    private void KeepVersionToCompare(
        IPlacedGetter record, RecordKey key, List<IPlacedGetter> versionsToCompare, bool isBelowTarget, List<PlacedVersionFact> versions)
    {
        versionsToCompare.Add(record);
        if (!isBelowTarget) return;
        _versionsToCompare.Remove(key);
        IPlacedGetter[] lowestFirst = [.. Enumerable.Reverse(versionsToCompare)];
        PluginName[] plugins = [.. versions.Select(version => version.Plugin).Reverse()];
        _partDifferences.Add(key, PlacedPartHistory.Of(_parts, lowestFirst, plugins));
    }

    /// <summary>A record the target plugin overrides is walked on, whatever the plugin, until its version just below the target plugin's is read.</summary>
    private bool StopsTheWalk(RecordKey key, PluginName plugin) => _stopsTheWalk(plugin) && !_versionsToCompare.ContainsKey(key);

    /// <returns>True when a link was read; outside the target spaces only links into the target plugin are read.</returns>
    private bool ReadLinks(IPlacedGetter record, RecordKey key, ScannedCell cell)
    {
        if (!cell.CollectReferences && !cell.InTargetSpace) return false;
        var linksBefore = _links.Count;
        LinkReader.ReadFromPlaced(record, key, linked => IsLinkRead(linked, cell), _names, _links, cell.InTargetSpace ? _linkedFromPlacedRecords : null);
        return _links.Count > linksBefore;
    }

    /// <summary>
    /// A plugin that can link to the target plugin is read for its links into it. In the target spaces every plugin is also read
    /// for its links into the records the target plugin overrides: the caller can judge such a record as a target object, and
    /// tells which placed records and cells of other plugins the target plugin's records link to.
    /// </summary>
    private bool IsLinkRead(RecordKey linked, ScannedCell cell) =>
        (cell.CollectReferences && linked.Plugin == _scope.Target)
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

    private PlacedVersionFact ReadVersion(IPlacedGetter record, ScannedCell cell) => new(
        cell.Plugin,
        new PlacementFacts(record.GetPresence(_names), record.Placement?.GetPosition(), record.Placement?.GetRotation()),
        record.Scale,
        record.GetBaseKey(_names),
        cell.Cell,
        cell.Space.Key,
        record.IsPersistent(),
        new ReferenceFacts(
            record is IPlacedObjectGetter { TeleportDestination: not null },
            record is IPlacedObjectGetter { Primitive: not null },
            record is IPlacedObjectGetter { MapMarker: not null },
            record.GetReferenceRadius(),
            record.GetPrimitiveBounds(),
            record.GetCollisionLayer(),
            record.IsCullingVolume()));

    private ScannedRecords CreateScannedRecords(IReadOnlyDictionary<RecordKey, CellAreaFact> cellAreas)
    {
        var terrain = new LandscapeTerrain(
            _landscapes,
            _landWorldspaceOf,
            _scope.TerrainVersions ? _landscapeVersions : null,
            _scope.TerrainLayers ? _names : null,
            ReadsLandVersions ? new LandscapesBelow(_scope.Target, _landscapesBelowTarget) : null);
        var navmeshes = _navmeshVersions?.Compare(_loadOrder);
        var changedCells = CompareCells();
        var changedLands = CompareLands();
        var facts = new PlacedRecordFacts(
            [.. _records.Select(WithVersions)],
            _recordsScanned,
            _footprint.OverriddenRecords,
            [.. _links],
            _navmeshes.Values.Sum(navmeshes => navmeshes.Count),
            cellAreas,
            _linkedFromPlacedRecords,
            changedCells,
            changedLands,
            navmeshes?.Facts ?? ImmutableArray<NavmeshRecordFact>.Empty,
            _deletedCells,
            navmeshes?.OverriddenDoorLinks ?? new Dictionary<RecordKey, NavDoorLinkVersions>(),
            _cellsDeletedInFinalWorld,
            _spaces);
        var water = new CellWater(_scope.Water ? _cellWaterCopies : null, CollectWorldspaceWater(), _waterWorldspaceOf);
        var areaHandles = CreateAreaRecordHandles(changedCells, changedLands, navmeshes?.Handles ?? NavmeshRecordHandles.Empty);
        var handles = new RecordHandles(_handles, _earlierRecords, _parts, areaHandles);
        return new ScannedRecords(facts, new ScanLookups(handles, terrain, water, new NavmeshRecords(_navmeshes)));
    }

    /// <returns>Only the cells whose target plugin's version differs from the one below it in a part.</returns>
    private ImmutableArray<CellRecordFact> CompareCells()
    {
        if (_areaTables is null) return [];
        return
        [
            .. _cellWalk.Read
                .Select(cell => new CellRecordFact(
                    cell.Key,
                    cell.Place.SpaceKey,
                    cell.Place.Grid,
                    cell.Versions.WinningMod,
                    CellPartHistory.Of(_areaTables.Cells, cell.Versions, _names),
                    cell.Versions.Versions.Any(version => version.IsDeleted),
                    WaterLevel.ReadCopy(cell.Versions.Plugins[0], cell.Versions.Earlier),
                    WaterLevel.ReadCopy(cell.Versions.WinningMod, cell.Versions.Winner),
                    DefaultWaterLevelOf(cell.Place)))
                .Where(fact => fact.Parts.Any(part => VersionComparison.NamedPluginVersionOf(part.Differences.Versions).FromBelow)),
        ];
    }

    /// <returns>The winning default water height of the worldspace whose water an exterior cell uses; null for an interior or where none is stored.</returns>
    private float? DefaultWaterLevelOf(CellPlace place) =>
        place.Grid is not null
        && _waterWorldspaceOf.TryGetValue(place.SpaceKey, out var supplier)
        && _linkCache.TryResolve<IWorldspaceGetter>(supplier.ToFormKey(), out var worldspace)
            ? worldspace.LandDefaults?.DefaultWaterHeight
            : null;

    /// <returns>Only the LAND records whose target plugin's version differs from the one below it in a part.</returns>
    private ImmutableArray<LandRecordFact> CompareLands()
    {
        if (_areaTables is null) return [];
        return
        [
            .. _landWalk.Read
                .Select(land => LandPartHistory.Of(_areaTables.Lands, land.Key, land.Place, land.Versions, _deletedLandCells.Contains(land.Place)))
                .Where(fact => !VersionComparison.NamedPluginVersionOf(fact.Versions).FromBelow.IsEmpty),
        ];
    }

    /// <remarks>Only the records with a fact keep their versions: nothing is written into the others.</remarks>
    private AreaRecordHandles? CreateAreaRecordHandles(ImmutableArray<CellRecordFact> cells, ImmutableArray<LandRecordFact> lands, NavmeshRecordHandles navmeshes) =>
        _areaTables is null
            ? null
            : new AreaRecordHandles(
                _cellWalk.VersionsOf(cells.Select(cell => cell.Key)), _landWalk.VersionsOf(lands.Select(land => land.Key)), navmeshes, _areaTables.Cells, _areaTables.Lands);

    private PlacedRecordFact WithVersions(PlacedRecordFact fact) => fact with
    {
        Versions = [.. Enumerable.Reverse(_versions[fact.Key])],
        PartDifferences = _partDifferences.TryGetValue(fact.Key, out var differences) ? differences : null,
    };

    /// <summary>What every record of one cell shares.</summary>
    /// <param name="Plugin">The plugin whose copy of the cell is walked.</param>
    private sealed record ScannedCell(SpaceFact Space, CellFact Cell, bool InTargetSpace, PluginName Plugin, bool CollectReferences);

    /// <summary>The part tables the cell and LAND records are compared with.</summary>
    private sealed record AreaRecordTables(CellRecordParts Cells, LandRecordParts Lands);

    /// <summary>Where a cell's winning version stands.</summary>
    /// <param name="Grid">Null for an interior.</param>
    private sealed record CellPlace(RecordKey SpaceKey, (int X, int Y)? Grid);

    /// <summary>
    /// Comparison space of a cell: its worldspace for exterior cells (including the worldspace's
    /// persistent TopCell, whose context parent is the worldspace), otherwise the interior cell itself.
    /// </summary>
    private RecordKey GetCellSpaceKey(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext) =>
        _names.KeyOf(CellPlaces.WorldspaceOf(cellContext)?.FormKey ?? cellContext.Record.FormKey);

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
        if (CellPlaces.WorldspaceOf(cellContext) is not { } found)
        {
            var cell = cellContext.Record;
            return new SpaceFact(key, cell.EditorID, SpaceKind.Interior, RecordDisplayNames.Of(cell.Name, cell.EditorID));
        }
        var worldspace = _linkCache.Resolve<IWorldspaceGetter>(found.FormKey);
        return new SpaceFact(key, worldspace.EditorID, SpaceKind.Worldspace, RecordDisplayNames.Of(worldspace.Name, worldspace.EditorID));
    }

}
