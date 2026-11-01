using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// The spaces (worldspace or interior cell keys) where the read's plugin places a record, one it created or one it overrides,
/// and every placed record from another plugin that the read's plugin overrides.
/// </summary>
/// <param name="OverriddenCells">
/// Only when the cell versions are asked for: the cell records of other plugins that the read's plugin overrides in the target
/// spaces, the persistent cell of a worldspace left out.
/// </param>
/// <param name="OverriddenLands">
/// Only when the LAND versions are asked for: the LAND records of other plugins that the read's plugin overrides in the worldspaces
/// whose land a target space uses.
/// </param>
/// <param name="OverriddenNavmeshes">
/// Only when the area record versions are asked for: the navmeshes of other plugins in the target spaces that the read's plugin
/// overrides without deleting them.
/// </param>
/// <param name="DeletedNavmeshes">Likewise, the navmeshes of other plugins in the target spaces that the read's plugin's version deletes.</param>
/// <param name="AddedNavmeshes">Likewise, the navmeshes the read's plugin itself defines in the target spaces.</param>
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

/// <summary>Reads the read's plugin's own cell tree for its <see cref="TargetFootprint"/>.</summary>
internal sealed class TargetFootprintReader(
    ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    PluginNames names,
    PlacedReadScope scope)
{
    private readonly ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> _loadOrder = loadOrder;
    private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache = linkCache;
    private readonly PluginNames _names = names;
    private readonly PlacedReadScope _scope = scope;

    /// <summary>
    /// Walks only the read's plugin's own cell tree, for the records of other plugins it overrides and the target spaces: every
    /// space where it places a record, its own or its override of another plugin's, which the caller can judge as a target object.
    /// A cell copy the read's plugin holds only to carry other data places nothing, so it adds no space.
    /// With the area record versions asked for, it also finds the cell and LAND records of other plugins it overrides, as far as
    /// asked for, and the navmeshes it overrides, deletes or adds.
    /// </summary>
    /// <returns>
    /// The footprint, its overridden cells already narrowed to the target spaces; and every overridden LAND record with the space of
    /// its cell, which the caller narrows once it knows the worldspaces whose land the target spaces use.
    /// </returns>
    public (TargetFootprint Footprint, List<(RecordKey Land, RecordKey Space)> OverriddenLands) AnalyzeTarget()
    {
        var footprint = new TargetFootprint([], [], [], [], [], [], []);
        var overriddenCells = new List<(RecordKey Cell, RecordKey Space)>();
        var overriddenLands = new List<(RecordKey Land, RecordKey Space)>();
        var navmeshes = new List<(INavigationMeshGetter Navmesh, RecordKey Key, RecordKey Space)>();
        if (_loadOrder.ListedOrder.FirstOrDefault(listing => _names.Of(listing.ModKey) == _scope.Plugin)?.Mod is not { } mod) return (footprint, overriddenLands);

        foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_linkCache))
        {
            var placesARecord = false;
            foreach (var (record, _) in cellContext.Record.EnumeratePlaced())
            {
                var recordKey = _names.KeyOf(record.FormKey);
                if (recordKey.Plugin != _scope.Plugin) footprint.OverriddenRecords.Add(recordKey);
                placesARecord = true;
            }
            var spaceKey = SpaceReader.GetCellSpaceKey(cellContext, _names);
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
            var kind = key.Plugin == _scope.Plugin ? footprint.AddedNavmeshes : navmesh.IsDeleted ? footprint.DeletedNavmeshes : footprint.OverriddenNavmeshes;
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
        if (areaReads.Cells && cellKey.Plugin != _scope.Plugin && (spaceKey == cellKey || cell.Grid is not null)) overriddenCells.Add((cellKey, spaceKey));
        if (areaReads.Lands && cell.Landscape is { } landscape && _names.KeyOf(landscape.FormKey) is var landKey && landKey.Plugin != _scope.Plugin)
        {
            overriddenLands.Add((landKey, spaceKey));
        }
    }
}
