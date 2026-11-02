using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// The navmeshes of the plugin spaces the read's plugin overrides, deletes or adds, each with its versions from the one just below
/// the read's plugin's up to the winner, and every plugin's navmesh info map, so a part is written from the versions it was compared on.
/// </summary>
/// <param name="navmeshes">Only those with a fact. For a navmesh the read's plugin defines, the version below its own holds no record.</param>
/// <param name="winners">Every navmesh of the plugin spaces at its winning version, so the written navmeshes can be checked among them.</param>
/// <param name="winningDoorLinks">The navmesh link of every placed record of the plugin spaces whose winning version has one.</param>
internal sealed class NavmeshRecordHandles(
    IReadOnlyDictionary<RecordKey, RecordVersions<NavmeshVersion?>> navmeshes,
    ImmutableArray<NavmeshMapVersion> maps,
    IReadOnlyDictionary<RecordKey, NavmeshVersion> winners,
    IReadOnlyDictionary<RecordKey, NavDoorLinkFact> winningDoorLinks)
{
    public IReadOnlyDictionary<RecordKey, RecordVersions<NavmeshVersion?>> Navmeshes { get; } = navmeshes;

    public ImmutableArray<NavmeshMapVersion> Maps { get; } = maps;

    public IReadOnlyDictionary<RecordKey, NavmeshVersion> Winners { get; } = winners;

    public IReadOnlyDictionary<RecordKey, NavDoorLinkFact> WinningDoorLinks { get; } = winningDoorLinks;

    /// <summary>The reverse links among <see cref="Winners"/> and <see cref="WinningDoorLinks"/>, built once for every final world over this read.</summary>
    public WinningNavmeshLinks Links { get; } = new(winners, winningDoorLinks);

    /// <summary>The read's plugin changes no navmesh, so no map is read.</summary>
    public static NavmeshRecordHandles Empty { get; } = new(
        ImmutableDictionary<RecordKey, RecordVersions<NavmeshVersion?>>.Empty,
        [],
        ImmutableDictionary<RecordKey, NavmeshVersion>.Empty,
        ImmutableDictionary<RecordKey, NavDoorLinkFact>.Empty);
}

/// <summary>
/// Collects, while the load order is walked highest priority first, the navmesh versions of the plugin spaces and the navmesh links
/// of their doors, then compares the versions of the navmeshes the read's plugin overrides, deletes or adds (<see cref="NavmeshRecordFact"/>).
/// Memory stays bounded: every navmesh keeps only its winner and its version just below the read's plugin's, and only the
/// navmeshes the read's plugin changed keep the versions in between.
/// </summary>
internal sealed class NavmeshVersionCollector(PluginName namedPlugin, PluginFootprint footprint, PluginNames names)
{
    private readonly NamedPluginOverrideWalk<NavmeshVersion?, RecordKey> _walk = new(namedPlugin);
    private readonly Dictionary<RecordKey, (PluginName Plugin, NavmeshVersion Version)> _winners = new();
    private readonly Dictionary<RecordKey, (PluginName Plugin, NavmeshVersion Version)> _earlier = new();
    private readonly Dictionary<RecordKey, NavDoorLinkFact> _winningDoorLinks = new();
    private readonly Dictionary<RecordKey, NavDoorLinkFact> _earlierDoorLinks = new();

    /// <summary>The cell whose child list holds the winning version of each placed record the read's plugin overrides.</summary>
    private readonly Dictionary<RecordKey, RecordKey> _winningDoorCells = new();

    /// <summary>Doors whose winner loads at or after the read's plugin and whose version below it is not read yet.</summary>
    private readonly HashSet<RecordKey> _doorsAwaitingEarlierVersion = new();

    /// <summary>The cells of the plugin spaces that some plugin's version deletes.</summary>
    private readonly HashSet<RecordKey> _deletedCells = new();

    /// <param name="belowNamedPlugin">The plugin loads before the read's plugin.</param>
    /// <remarks>Like placed records, a plugin's first copy of a navmesh found is the one kept.</remarks>
    public void SeeNavmeshes(ICellGetter cell, RecordKey cellKey, RecordKey spaceKey, PluginName plugin, bool belowNamedPlugin)
    {
        if (cell.IsDeleted) _deletedCells.Add(cellKey);
        var place = CellPlaces.PlaceOf(cell, cellKey, spaceKey);
        foreach (var navmesh in cell.NavigationMeshes)
        {
            var key = names.KeyOf(navmesh.FormKey);
            var version = new NavmeshVersion(navmesh, cellKey, place);
            if (footprint.ChangesNavmesh(key)) _walk.Keep(key, plugin, version, spaceKey);
            _winners.TryAdd(key, (plugin, version));
            if (belowNamedPlugin) _earlier.TryAdd(key, (plugin, version));
        }
    }

    /// <param name="cell">The cell whose child list holds the version.</param>
    /// <param name="isWinner">The first version of the record found.</param>
    /// <param name="belowNamedPlugin">The version's plugin loads before the read's plugin.</param>
    public void SeeDoorVersion(RecordKey key, IPlacedGetter record, RecordKey cell, bool isWinner, bool belowNamedPlugin)
    {
        if (record is not IPlacedObjectGetter placed) return;
        var link = placed.NavigationDoorLink is { } doorLink ? new NavDoorLinkFact(names.KeyOf(doorLink.NavMesh.FormKey), doorLink.TeleportMarkerTriangle) : (NavDoorLinkFact?)null;
        if (isWinner)
        {
            if (footprint.OverriddenRecords.Contains(key)) _winningDoorCells.Add(key, cell);
            if (link is { } winning) _winningDoorLinks.Add(key, winning);
            if (!belowNamedPlugin)
            {
                _doorsAwaitingEarlierVersion.Add(key);
                return;
            }
        }
        else if (!belowNamedPlugin || !_doorsAwaitingEarlierVersion.Remove(key)) return;
        if (link is { } earlier) _earlierDoorLinks.Add(key, earlier);
    }

    /// <returns>
    /// The navmeshes whose read's plugin's version differs from the one below it in a part, in key order; and the navmesh link of
    /// each placed record the read's plugin overrides that has one in its version below the read's plugin's or its winning version, by key.
    /// </returns>
    public (ImmutableArray<NavmeshRecordFact> Facts, NavmeshRecordHandles Handles, IReadOnlyDictionary<RecordKey, NavDoorLinkVersions> OverriddenDoorLinks) Compare(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder)
    {
        var changed = ChangedNavmeshVersions();
        var linkingInto = NavmeshesLinkingInto(changed.Keys.ToHashSet());
        var neighbours = changed.ToDictionary(entry => entry.Key, entry => NeighboursOf(entry.Key, entry.Value.Versions, linkingInto));
        var maps = NavmeshMaps.Read(loadOrder, changed.Keys.Concat(neighbours.Values.SelectMany(keys => keys)).ToHashSet(), names);
        var comparing = new Comparing(maps, DoorsNamingEachNavmesh(), new Dictionary<RecordKey, NeighbourNavmesh>());
        var compared = changed
            .OrderBy(entry => entry.Key, RecordKeyOrder.Comparer)
            .Select(entry => CompareNavmesh(entry.Key, entry.Value.SpaceKey, entry.Value.Versions, neighbours[entry.Key], comparing))
            .ToList();
        ImmutableArray<NavmeshRecordFact> facts = [.. compared.Where(NamedPluginChangedAPart)];
        var handles = new NavmeshRecordHandles(
            facts.ToDictionary(fact => fact.Key, fact => changed[fact.Key].Versions),
            maps.Versions,
            _winners.ToDictionary(entry => entry.Key, entry => entry.Value.Version),
            _winningDoorLinks);
        return (facts, handles, OverriddenDoorLinks());
    }

    private Dictionary<RecordKey, NavDoorLinkVersions> OverriddenDoorLinks() =>
        _winningDoorLinks.Keys.Concat(_earlierDoorLinks.Keys)
            .Where(footprint.OverriddenRecords.Contains)
            .Distinct()
            .ToDictionary(door => door, DoorLinkOf);

    private NavDoorLinkVersions DoorLinkOf(RecordKey door) => new(
        door,
        _earlierDoorLinks.TryGetValue(door, out var earlier) ? earlier : null,
        _winningDoorLinks.TryGetValue(door, out var current) ? current : null,
        _winningDoorCells.TryGetValue(door, out var cell) && _deletedCells.Contains(cell));

    private Dictionary<RecordKey, (RecordKey SpaceKey, RecordVersions<NavmeshVersion?> Versions)> ChangedNavmeshVersions()
    {
        var changed = _walk.Read.ToDictionary(entry => entry.Key, entry => (entry.Place, entry.Versions));
        foreach (var (key, spaceKey, plugins, versions) in _walk.DefinedByNamedPlugin)
        {
            // No record below the read's plugin's version, which stands under the read's plugin's name.
            changed.Add(key, (spaceKey, new RecordVersions<NavmeshVersion?>([namedPlugin, .. plugins], [null, .. versions])));
        }
        return changed;
    }

    /// <summary>For each changed navmesh, the winning navmeshes whose edge-link table names it.</summary>
    /// <remarks>Without a changed navmesh no edge-link table is read: the collector then runs only for the door links.</remarks>
    private Dictionary<RecordKey, List<RecordKey>> NavmeshesLinkingInto(IReadOnlySet<RecordKey> changed)
    {
        var linkingInto = new Dictionary<RecordKey, List<RecordKey>>();
        if (changed.Count == 0) return linkingInto;
        foreach (var (key, winner) in _winners)
        {
            foreach (var linked in NavmeshShapes.LinkedNavmeshes(winner.Version.Record, names).Where(changed.Contains))
            {
                KeyedGroups.GetOrAddList(linkingInto, linked).Add(key);
            }
        }
        return linkingInto;
    }

    /// <summary>The navmeshes of the plugin spaces linked to the navmesh by its version below the read's plugin's, by the read's plugin's, or by their winner.</summary>
    private ImmutableArray<RecordKey> NeighboursOf(RecordKey key, RecordVersions<NavmeshVersion?> read, Dictionary<RecordKey, List<RecordKey>> linkingInto)
    {
        var linked = new HashSet<RecordKey>(linkingInto.GetValueOrDefault(key) ?? []);
        foreach (var version in new[] { read.Earlier, read.OfNamedPlugin })
        {
            if (version is not null) linked.UnionWith(NavmeshShapes.LinkedNavmeshes(version.Record, names));
        }
        linked.Remove(key);
        return [.. linked.Where(_winners.ContainsKey).Order(RecordKeyOrder.Comparer)];
    }

    /// <summary>For each navmesh, the doors whose winning link or link below the read's plugin names it.</summary>
    private Dictionary<RecordKey, List<RecordKey>> DoorsNamingEachNavmesh()
    {
        var doors = new Dictionary<RecordKey, List<RecordKey>>();
        foreach (var (door, link) in _winningDoorLinks.Concat(_earlierDoorLinks)) KeyedGroups.GetOrAddList(doors, link.Navmesh).Add(door);
        return doors;
    }

    private NavmeshRecordFact CompareNavmesh(RecordKey key, RecordKey spaceKey, RecordVersions<NavmeshVersion?> read, ImmutableArray<RecordKey> neighbours, Comparing comparing)
    {
        var own = read.OfNamedPlugin!;
        var (geometry, presence) = NavmeshPartComparison.Of(read);
        var earlier = read.Earlier is { } below ? NavmeshShapes.Read(below, read.Plugins[0], comparing.Maps.EntryBelow(key, namedPlugin), names) : null;
        var ownEntry = comparing.Maps.EntryUpTo(key, namedPlugin);
        var current = own.Record.IsDeleted && earlier is not null
            ? earlier with { Plugin = namedPlugin, Cell = own.Cell, IsDeleted = true, MapEntry = ownEntry }
            : NavmeshShapes.Read(own, namedPlugin, ownEntry, names);
        return new NavmeshRecordFact(
            key,
            spaceKey,
            read.Winner!.Cell,
            read.WinningMod,
            CaseOf(key),
            geometry,
            presence,
            comparing.Maps.EntryDifferences(key, namedPlugin),
            IsDeletedAboveNamedPlugin(read, own),
            earlier,
            current,
            [.. neighbours.Select(neighbour => NeighbourOf(neighbour, comparing))],
            DoorsOf(key, [earlier, current], comparing),
            comparing.Maps.WinningPreferredPaths.Contains(key),
            comparing.Maps.WinningMapMod,
            _deletedCells.Contains(read.Winner!.Cell),
            read.WinningMod == namedPlugin ? null :NavmeshShapes.Read(read.Winner!, read.WinningMod, comparing.Maps.WinningEntry(key), names),
            comparing.Maps.WinningEntry(key));
    }

    private NavmeshCase CaseOf(RecordKey key) =>
        footprint.AddedNavmeshes.Contains(key) ? NavmeshCase.Added
        : footprint.DeletedNavmeshes.Contains(key) ? NavmeshCase.Deleted
        : NavmeshCase.Edited;

    private static bool IsDeletedAboveNamedPlugin(RecordVersions<NavmeshVersion?> read, NavmeshVersion own) =>
        !own.Record.IsDeleted && VersionComparison.AboveNamedPlugin(read.Versions).Any(version => version!.Record.IsDeleted);

    private NeighbourNavmesh NeighbourOf(RecordKey neighbour, Comparing comparing)
    {
        if (comparing.Neighbours.TryGetValue(neighbour, out var known)) return known;
        var (winningPlugin, winner) = _winners[neighbour];
        var read = new NeighbourNavmesh(
            neighbour,
            NavmeshShapes.Read(winner, winningPlugin, comparing.Maps.WinningEntry(neighbour), names),
            _earlier.TryGetValue(neighbour, out var earlier) ? NavmeshShapes.Read(earlier.Version, earlier.Plugin, comparing.Maps.EntryBelow(neighbour, namedPlugin), names) : null);
        comparing.Neighbours.Add(neighbour, read);
        return read;
    }

    /// <summary>The doors the navmesh's door triangles or map entries name, and the doors whose navmesh link names it.</summary>
    private ImmutableArray<NavDoorLinkVersions> DoorsOf(RecordKey key, NavmeshShape?[] shapes, Comparing comparing)
    {
        var doors = new HashSet<RecordKey>(comparing.DoorsNaming.GetValueOrDefault(key) ?? []);
        foreach (var shape in shapes.OfType<NavmeshShape>())
        {
            doors.UnionWith(shape.DoorTriangles.Select(door => door.Door));
            doors.UnionWith(shape.MapEntry?.LinkedDoors ?? []);
        }
        doors.ExceptWith([RecordKey.Null, RecordKey.None]);
        return [.. doors.Order(RecordKeyOrder.Comparer).Select(DoorLinkOf)];
    }

    private static bool NamedPluginChangedAPart(NavmeshRecordFact fact) =>
        NamedPluginChanged(fact.Geometry) || NamedPluginChanged(fact.Presence) || (fact.MapEntry is { } mapEntry && NamedPluginChanged(mapEntry));

    private static bool NamedPluginChanged(PartDifferences part) => VersionComparison.NamedPluginVersionOf(part.Versions).FromBelow;

    /// <summary>What comparing every changed navmesh shares.</summary>
    /// <param name="Neighbours">The neighbours read so far, so each is read once.</param>
    private sealed record Comparing(NavmeshMaps Maps, Dictionary<RecordKey, List<RecordKey>> DoorsNaming, Dictionary<RecordKey, NeighbourNavmesh> Neighbours);
}
