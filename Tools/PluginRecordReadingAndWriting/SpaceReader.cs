using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Reads the space and cell records met on the load order walk: each space (worldspace or interior cell) at its first sighting,
/// the worldspaces whose land and water each target space uses, the winning cells of the target spaces, and, with the area record
/// versions asked for, the cells some plugin's version deletes and the versions of the cells of other plugins the read's plugin
/// overrides, compared (<see cref="CellPartComparison"/>).
/// </summary>
internal sealed class SpaceReader
{
    private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;
    private readonly PluginNames _names;
    private readonly TargetFootprint _footprint;

    /// <summary>The part table the cells are compared with; null when the area record versions were not asked for.</summary>
    private readonly CellRecordParts? _cellParts;

    private readonly Dictionary<RecordKey, ICellGetter> _winningCells = new();
    private readonly Dictionary<RecordKey, SpaceFact> _spaces = new();
    private readonly List<SpaceCell> _targetSpaceCells = [];
    private readonly Dictionary<RecordKey, RecordKey> _landWorldspaceOf = new();
    private readonly Dictionary<RecordKey, RecordKey> _waterWorldspaceOf = new();

    /// <summary>With the area record versions asked for, the cells of the target spaces that some plugin's version deletes.</summary>
    private readonly HashSet<RecordKey> _deletedCells = new();

    /// <summary>The cells of the target spaces whose winning version deletes them, with that version's plugin.</summary>
    private readonly Dictionary<RecordKey, PluginName> _cellsDeletedInFinalWorld = new();

    private readonly TargetOverrideWalk<ICellGetter, CellPlace> _cellWalk;

    public SpaceReader(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, PluginNames names, TargetFootprint footprint, PluginName target, CellRecordParts? cellParts)
    {
        _linkCache = linkCache;
        _names = names;
        _footprint = footprint;
        _cellParts = cellParts;
        _cellWalk = new(target);
    }

    public IReadOnlyDictionary<RecordKey, SpaceFact> Spaces => _spaces;

    /// <summary>The winning cells of the target spaces, in the order found.</summary>
    public IReadOnlyList<SpaceCell> TargetSpaceCells => _targetSpaceCells;

    /// <summary>For each target worldspace, the worldspace whose land it uses.</summary>
    public IReadOnlyDictionary<RecordKey, RecordKey> LandWorldspaceOf => _landWorldspaceOf;

    /// <summary>For each target worldspace, the worldspace whose water it uses.</summary>
    public IReadOnlyDictionary<RecordKey, RecordKey> WaterWorldspaceOf => _waterWorldspaceOf;

    public IReadOnlySet<RecordKey> DeletedCells => _deletedCells;

    public IReadOnlyDictionary<RecordKey, PluginName> CellsDeletedInFinalWorld => _cellsDeletedInFinalWorld;

    public void FindLandAndWaterWorldspaces()
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

    /// <summary>Keeps the cell as the winning cell of a target space, as a version of a cell the read's plugin overrides and as a deleted cell, as far as each applies.</summary>
    public void SeeCell(ICellGetter cell, RecordKey cellKey, SpaceFact space, PluginName plugin, bool inTargetSpace)
    {
        if (inTargetSpace && _winningCells.TryAdd(cellKey, cell)) KeepWinningCell(cell, cellKey, space, plugin);
        if (_footprint.OverriddenCells.Contains(cellKey)) _cellWalk.Keep(cellKey, plugin, cell, new CellPlace(space.Key, GridOf(cell, cellKey, space)));
        if (_cellParts is not null && inTargetSpace && cell.IsDeleted) _deletedCells.Add(cellKey);
    }

    /// <param name="cellKey">A cell of a target space.</param>
    public ICellGetter WinningCellOf(RecordKey cellKey) => _winningCells[cellKey];

    private void KeepWinningCell(ICellGetter cell, RecordKey cellKey, SpaceFact space, PluginName plugin)
    {
        _targetSpaceCells.Add(new SpaceCell(cellKey, space, cell));
        if (cell.IsDeleted) _cellsDeletedInFinalWorld.Add(cellKey, plugin);
    }

    /// <returns>Only the cells whose read's plugin's version differs from the one below it in a part.</returns>
    public ImmutableArray<CellRecordFact> CompareCells()
    {
        if (_cellParts is null) return [];
        return
        [
            .. _cellWalk.Read
                .Select(cell => new CellRecordFact(
                    cell.Key,
                    cell.Place.SpaceKey,
                    cell.Place.Grid,
                    cell.Versions.WinningMod,
                    CellPartComparison.Of(_cellParts, cell.Versions, _names),
                    cell.Versions.Versions.Any(version => version.IsDeleted),
                    WaterLevel.ReadCopy(cell.Versions.Plugins[0], cell.Versions.Earlier),
                    WaterLevel.ReadCopy(cell.Versions.WinningMod, cell.Versions.Winner),
                    DefaultWaterLevelOf(cell.Place)))
                .Where(fact => fact.Parts.Any(part => VersionComparison.NamedPluginVersionOf(part.Differences.Versions).FromBelow)),
        ];
    }

    /// <param name="cells">Cells <see cref="CompareCells"/> gave.</param>
    public IReadOnlyDictionary<RecordKey, RecordVersions<ICellGetter>> CellVersionsOf(IEnumerable<RecordKey> cells) => _cellWalk.VersionsOf(cells);

    /// <returns>The winning default water height of the worldspace whose water an exterior cell uses; null for an interior or where none is stored.</returns>
    private float? DefaultWaterLevelOf(CellPlace place) =>
        place.Grid is not null
        && _waterWorldspaceOf.TryGetValue(place.SpaceKey, out var supplier)
        && _linkCache.TryResolve<IWorldspaceGetter>(supplier.ToFormKey(), out var worldspace)
            ? worldspace.LandDefaults?.DefaultWaterHeight
            : null;

    /// <summary>
    /// Comparison space of a cell: its worldspace for exterior cells (including the worldspace's
    /// persistent TopCell, whose context parent is the worldspace), otherwise the interior cell itself.
    /// </summary>
    public static RecordKey GetCellSpaceKey(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext, PluginNames names) =>
        names.KeyOf(CellPlaces.WorldspaceOf(cellContext)?.FormKey ?? cellContext.Record.FormKey);

    /// <remarks>Read at the space's first sighting, which for an interior is its winning cell.</remarks>
    public SpaceFact GetCellSpace(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        var key = GetCellSpaceKey(cellContext, _names);
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

    /// <returns>Null for an interior and for a worldspace's persistent cell.</returns>
    public static (int X, int Y)? GridOf(ICellGetter cell, RecordKey cellKey, SpaceFact space) =>
        space.Key != cellKey && cell.Grid is { } grid ? (grid.Point.X, grid.Point.Y) : null;

    /// <summary>Where a cell's winning version stands.</summary>
    /// <param name="Grid">Null for an interior.</param>
    private sealed record CellPlace(RecordKey SpaceKey, (int X, int Y)? Grid);
}
