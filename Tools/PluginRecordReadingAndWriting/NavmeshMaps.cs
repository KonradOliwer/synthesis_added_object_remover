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
    private readonly PluginName _target;
    private readonly int _targetIndex;
    private readonly PluginNames _names;
    private readonly ImmutableArray<NavmeshMapVersion> _maps;
    private readonly Dictionary<RecordKey, List<(NavmeshMapVersion Map, INavigationMapInfoGetter Entry)>> _entries = new();

    private NavmeshMaps(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, IReadOnlySet<RecordKey> navmeshes, PluginName target, PluginNames names)
    {
        _target = target;
        _names = names;
        var listings = loadOrder.ListedOrder.ToList();
        _targetIndex = listings.FindIndex(listing => names.Of(listing.ModKey) == target);
        _maps = [.. listings.SelectMany((listing, index) => MapsOf(listing, index))];
        KeepEntriesOf(navmeshes);
        WinningPreferredPaths = PreferredPathsOf(_maps.LastOrDefault());
    }

    /// <summary>Every plugin's map, in load order.</summary>
    public ImmutableArray<NavmeshMapVersion> Versions => _maps;

    /// <param name="navmeshes">The navmeshes whose entries are kept.</param>
    public static NavmeshMaps Read(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, IReadOnlySet<RecordKey> navmeshes, PluginName target, PluginNames names) =>
        new(loadOrder, navmeshes, target, names);

    /// <summary>
    /// What the target plugin last changed in the navmesh's entry; <see cref="PartOutcome.Unchanged"/> when its map carries none. With
    /// no entry below the target plugin's, the target plugin's entry counts as a change.
    /// </summary>
    public PartOutcome EntryOutcome(RecordKey navmesh, PluginName patch)
    {
        var entries = EntriesOf(navmesh);
        if (entries.FirstOrDefault(entry => entry.Map.LoadIndex == _targetIndex) is not { Entry: { } own }) return new PartOutcome.Unchanged();
        var below = entries.LastOrDefault(entry => entry.Map.LoadIndex < _targetIndex);
        var above = entries.Where(entry => entry.Map.LoadIndex > _targetIndex).ToList();
        INavigationMapInfoGetter?[] versions = [below.Entry, own, .. above.Select(entry => entry.Entry)];
        // With no entry below, index 0 holds none; PartHistory names only the plugins above the target plugin's.
        PluginName[] plugins = [below.Entry is null ? _target : below.Map.Plugin, _target, .. above.Select(entry => entry.Map.Plugin)];
        return PartHistory.Of(plugins, PartHistory.TargetVersionIndex, patch, (lower, higher) => NavmeshRecordParts.MapEntry.DiffersOrMissing(versions[lower], versions[higher]));
    }

    /// <summary>The entry of the last plugin below the target plugin that carries one.</summary>
    public NavmeshMapEntryFact? EntryBelowTarget(RecordKey navmesh) => LastEntry(navmesh, loadIndex => loadIndex < _targetIndex);

    /// <summary>The entry of the last plugin up to and including the target plugin that carries one.</summary>
    public NavmeshMapEntryFact? EntryUpToTarget(RecordKey navmesh) => LastEntry(navmesh, loadIndex => loadIndex <= _targetIndex);

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
