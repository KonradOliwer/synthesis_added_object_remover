using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Reads the LAND records of the worldspaces whose land a target space uses and the water of the target spaces, as the terrain and
/// water lookups need them; and, with the LAND area record versions asked for, the versions of the LAND records of other plugins
/// the read's plugin overrides, compared (<see cref="LandPartComparison"/>).
/// </summary>
internal sealed class LandscapeReader
{
    private readonly ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> _loadOrder;
    private readonly PluginNames _names;
    private readonly PlacedReadScope _scope;
    private readonly TargetFootprint _footprint;
    private readonly IReadOnlyDictionary<RecordKey, RecordKey> _landWorldspaceOf;
    private readonly IReadOnlyDictionary<RecordKey, RecordKey> _waterWorldspaceOf;
    private readonly HashSet<RecordKey> _landWorldspaces;

    /// <summary>The part table the LAND records are compared with; null when the area record versions were not asked for.</summary>
    private readonly LandRecordParts? _landParts;

    private readonly Dictionary<ExteriorCell, ILandscapeGetter> _landscapes = new();
    private readonly Dictionary<ExteriorCell, ILandscapeGetter> _landscapesBelowTarget = new();
    private readonly Dictionary<ExteriorCell, List<LandscapeVersion>> _landscapeVersions = new();
    private readonly Dictionary<ExteriorCell, List<CellWaterCopy>> _cellWaterCopies = new();

    /// <summary>With the area record versions asked for, the cells of the land worldspaces that some plugin's version deletes.</summary>
    private readonly HashSet<ExteriorCell> _deletedLandCells = new();

    private readonly TargetOverrideWalk<ILandscapeGetter, ExteriorCell> _landWalk;

    /// <param name="scope">As narrowed by the footprint.</param>
    /// <param name="landWorldspaceOf">For each target worldspace, the worldspace whose land it uses.</param>
    /// <param name="waterWorldspaceOf">For each target worldspace, the worldspace whose water it uses.</param>
    public LandscapeReader(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder,
        PluginNames names,
        PlacedReadScope scope,
        TargetFootprint footprint,
        IReadOnlyDictionary<RecordKey, RecordKey> landWorldspaceOf,
        IReadOnlyDictionary<RecordKey, RecordKey> waterWorldspaceOf,
        LandRecordParts? landParts)
    {
        _loadOrder = loadOrder;
        _names = names;
        _scope = scope;
        _footprint = footprint;
        _landWorldspaceOf = landWorldspaceOf;
        _waterWorldspaceOf = waterWorldspaceOf;
        _landWorldspaces = landWorldspaceOf.Values.ToHashSet();
        _landParts = landParts;
        _landWalk = new(scope.Plugin);
    }

    /// <summary>The worldspaces whose land a target space uses.</summary>
    public IReadOnlySet<RecordKey> LandWorldspaces => _landWorldspaces;

    /// <summary>The LAND area record versions are asked for.</summary>
    private bool ReadsLandVersions => _scope.AreaRecordVersions is { Lands: true };

    /// <summary>Keeps the land and water of an exterior cell, as far as asked for.</summary>
    /// <param name="belowTarget">The cell's plugin loads before the read's plugin.</param>
    public void SeeCell(ICellGetter cell, RecordKey cellKey, SpaceFact space, PluginName plugin, bool belowTarget)
    {
        if (ExteriorCellOf(cell, cellKey, space) is not { } exteriorCell) return;
        if (_landParts is not null && cell.IsDeleted && _landWorldspaces.Contains(space.Key)) _deletedLandCells.Add(exteriorCell);
        if (_landWorldspaces.Contains(space.Key)) CollectLandscape(cell, exteriorCell, plugin, belowTarget);
        if (_scope.Water && _waterWorldspaceOf.ContainsKey(space.Key)) CollectCellWater(cell, exteriorCell, plugin);
    }

    /// <remarks>
    /// LAND is a record of its own inside the cell, so a cell override without it leaves the
    /// terrain to a lower-priority plugin; the first cell copy found that carries one is the winner.
    /// With the terrain versions asked for, each plugin's copy is also kept, undecoded, in the order found.
    /// </remarks>
    private void CollectLandscape(ICellGetter cell, ExteriorCell exteriorCell, PluginName plugin, bool belowTarget)
    {
        if (cell.Landscape is not { } landscape) return;
        _landscapes.TryAdd(exteriorCell, landscape);
        if (belowTarget && ReadsLandVersions) _landscapesBelowTarget.TryAdd(exteriorCell, landscape);
        if (_scope.TerrainVersions) KeyedGroups.GetOrAddList(_landscapeVersions, exteriorCell).Add(new LandscapeVersion(plugin, landscape));
        if (_footprint.OverriddenLands.Count > 0 && _names.KeyOf(landscape.FormKey) is var landKey && _footprint.OverriddenLands.Contains(landKey))
        {
            _landWalk.Keep(landKey, plugin, landscape, exteriorCell);
        }
    }

    /// <remarks>
    /// The water fields are part of the cell record itself, so they are read from the cells of the target spaces' own worldspaces, not
    /// of the worldspace whose land they use. Every plugin's copy of the cell is kept, with or without land, in the order found;
    /// like the land, two cells at one grid in one plugin are both kept.
    /// </remarks>
    private void CollectCellWater(ICellGetter cell, ExteriorCell exteriorCell, PluginName plugin) =>
        KeyedGroups.GetOrAddList(_cellWaterCopies, exteriorCell).Add(WaterLevel.ReadCopy(plugin, cell));

    /// <returns>Null for a worldspace's persistent cell and any other cell without a grid.</returns>
    private static ExteriorCell? ExteriorCellOf(ICellGetter cell, RecordKey cellKey, SpaceFact space) =>
        space.Key != cellKey && cell.Grid is { } grid ? new ExteriorCell(space.Key, grid.Point.X, grid.Point.Y) : null;

    public LandscapeTerrain CreateTerrain() => new(
        _landscapes,
        _landWorldspaceOf,
        _scope.TerrainVersions ? _landscapeVersions : null,
        _scope.TerrainLayers ? _names : null,
        ReadsLandVersions ? new LandscapesBelow(_scope.Plugin, _landscapesBelowTarget) : null);

    public CellWater CreateWater() => new(_scope.Water ? _cellWaterCopies : null, CollectWorldspaceWater(), _waterWorldspaceOf);

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

    /// <returns>Only the LAND records whose read's plugin's version differs from the one below it in a part.</returns>
    public ImmutableArray<LandRecordFact> CompareLands()
    {
        if (_landParts is null) return [];
        return
        [
            .. _landWalk.Read
                .Select(land => LandPartComparison.Of(_landParts, land.Key, land.Place, land.Versions, _deletedLandCells.Contains(land.Place)))
                .Where(fact => !VersionComparison.NamedPluginVersionOf(fact.Versions).FromBelow.IsEmpty),
        ];
    }

    /// <param name="lands">LAND records <see cref="CompareLands"/> gave.</param>
    public IReadOnlyDictionary<RecordKey, RecordVersions<ILandscapeGetter>> LandVersionsOf(IEnumerable<RecordKey> lands) => _landWalk.VersionsOf(lands);
}
