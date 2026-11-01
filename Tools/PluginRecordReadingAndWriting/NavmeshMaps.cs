using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A plugin's navmesh info map (NAVI) and the plugin's place in the load order.</summary>
internal sealed record NavmeshMapVersion(int LoadIndex, PluginName Plugin, INavigationMeshInfoMapGetter Map);

/// <summary>
/// The navmesh info maps (NAVI) of the load order, plugin by plugin in load order, with the entries they carry for the navmeshes
/// asked for. Each plugin's map carries only the entries of the navmeshes it changes and the game merges them per navmesh, so a
/// navmesh's entry versions are the entries of the plugins whose map carries one, the later plugin's winning.
/// </summary>
internal sealed class NavmeshMaps
{
    private readonly PluginNames _names;
    private readonly IReadOnlyDictionary<PluginName, int> _loadIndexOf;
    private readonly ImmutableArray<NavmeshMapVersion> _maps;
    private readonly Dictionary<RecordKey, List<(NavmeshMapVersion Map, INavigationMapInfoGetter Entry)>> _entries = new();

    private NavmeshMaps(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, IReadOnlySet<RecordKey> navmeshes, PluginNames names)
    {
        _names = names;
        var listings = loadOrder.ListedOrder.ToList();
        _loadIndexOf = listings.Select((listing, index) => (Plugin: names.Of(listing.ModKey), index)).DistinctBy(entry => entry.Plugin).ToDictionary(entry => entry.Plugin, entry => entry.index);
        _maps = [.. listings.SelectMany((listing, index) => MapsOf(listing, index))];
        KeepEntriesOf(navmeshes);
        WinningPreferredPaths = PreferredPathsOf(_maps.LastOrDefault());
    }

    /// <summary>Every plugin's map, in load order.</summary>
    public ImmutableArray<NavmeshMapVersion> Versions => _maps;

    /// <param name="navmeshes">The navmeshes whose entries are kept.</param>
    public static NavmeshMaps Read(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, IReadOnlySet<RecordKey> navmeshes, PluginNames names) =>
        new(loadOrder, navmeshes, names);

    /// <summary>
    /// How the navmesh's entry differs across the entries of the plugins whose map carries one, from the last one below the given
    /// plugin up to the last in load order; null when the given plugin's map carries none. With no entry below the given plugin's,
    /// the lowest version holds none, under the given plugin's name.
    /// </summary>
    public PartDifferences? EntryDifferences(RecordKey navmesh, PluginName plugin)
    {
        var pluginIndex = _loadIndexOf[plugin];
        var entries = EntriesOf(navmesh);
        if (entries.FirstOrDefault(entry => entry.Map.LoadIndex == pluginIndex) is not { Entry: { } own }) return null;
        var below = entries.LastOrDefault(entry => entry.Map.LoadIndex < pluginIndex);
        var above = entries.Where(entry => entry.Map.LoadIndex > pluginIndex).ToList();
        INavigationMapInfoGetter?[] versions = [below.Entry, own, .. above.Select(entry => entry.Entry)];
        PluginName[] plugins = [below.Entry is null ? plugin : below.Map.Plugin, plugin, .. above.Select(entry => entry.Map.Plugin)];
        return VersionComparison.ComparePart(
            plugins, (lower, higher) => NavmeshRecordParts.MapEntry.DiffersOrMissing(versions[lower], versions[higher]), lowestNotCopyable: false);
    }

    /// <summary>The entry of the last plugin below the given plugin that carries one.</summary>
    public NavmeshMapEntryFact? EntryBelow(RecordKey navmesh, PluginName plugin)
    {
        var pluginIndex = _loadIndexOf[plugin];
        return LastEntry(navmesh, loadIndex => loadIndex < pluginIndex);
    }

    /// <summary>The entry of the last plugin up to and including the given plugin that carries one.</summary>
    public NavmeshMapEntryFact? EntryUpTo(RecordKey navmesh, PluginName plugin)
    {
        var pluginIndex = _loadIndexOf[plugin];
        return LastEntry(navmesh, loadIndex => loadIndex <= pluginIndex);
    }

    public NavmeshMapEntryFact? WinningEntry(RecordKey navmesh) => LastEntry(navmesh, static _ => true);

    /// <summary>The plugin of the last map in load order; null when no plugin has one.</summary>
    public PluginName? WinningMapMod => _maps.IsEmpty ? null : _maps[^1].Plugin;

    /// <summary>The navmeshes the preferred paths (NVPP) of the last plugin's map name.</summary>
    public IReadOnlySet<RecordKey> WinningPreferredPaths { get; }

    /// <remarks>Without a navmesh asked for no entry is read, so a run that changes no navmesh walks no map's entries.</remarks>
    private void KeepEntriesOf(IReadOnlySet<RecordKey> navmeshes)
    {
        if (navmeshes.Count == 0) return;
        foreach (var map in _maps)
        {
            foreach (var entry in map.Map.MapInfos)
            {
                if (_names.KeyOf(entry.NavigationMesh.FormKey) is var key && navmeshes.Contains(key)) KeyedGroups.GetOrAddList(_entries, key).Add((map, entry));
            }
        }
    }

    private IEnumerable<NavmeshMapVersion> MapsOf(IModListingGetter<ISkyrimModGetter> listing, int loadIndex) =>
        listing.Mod?.NavigationMeshInfoMaps.Select(map => new NavmeshMapVersion(loadIndex, _names.Of(listing.ModKey), map)) ?? [];

    private List<(NavmeshMapVersion Map, INavigationMapInfoGetter Entry)> EntriesOf(RecordKey navmesh) =>
        _entries.TryGetValue(navmesh, out var entries) ? entries : [];

    private NavmeshMapEntryFact? LastEntry(RecordKey navmesh, Func<int, bool> isAtLoadIndex) =>
        EntriesOf(navmesh).LastOrDefault(entry => isAtLoadIndex(entry.Map.LoadIndex)) is { Entry: { } entry, Map: var map }
            ? NavmeshShapes.ReadEntry(map.Plugin, _names.KeyOf(map.Map.FormKey), entry, _names)
            : null;

    private HashSet<RecordKey> PreferredPathsOf(NavmeshMapVersion? map)
    {
        if (map?.Map.PreferredPathing is not { } paths) return [];
        return
        [
            .. paths.NavmeshSets.SelectMany(set => set.Navmeshes).Select(navmesh => _names.KeyOf(navmesh.FormKey)),
            .. paths.NavmeshTree.Select(node => _names.KeyOf(node.NavMesh.FormKey)),
        ];
    }
}
