using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// The navmeshes of the target spaces the target plugin overrides, deletes or adds, each with its versions from the one just below
/// the target plugin's up to the winner, and every plugin's navmesh info map, so a part is written from the versions it was judged on.
/// </summary>
/// <param name="navmeshes">Only those with a fact. For a navmesh the target plugin defines, the version below its own holds no record.</param>
/// <param name="winners">Every navmesh of the target spaces at its winning version, so the written navmeshes can be checked among them.</param>
/// <param name="winningDoorLinks">The navmesh link of every placed record of the target spaces whose winning version has one.</param>
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

    /// <summary>The target plugin changes no navmesh, so no map is read.</summary>
    public static NavmeshRecordHandles Empty { get; } = new(
        new Dictionary<RecordKey, RecordVersions<NavmeshVersion?>>(), [], new Dictionary<RecordKey, NavmeshVersion>(), new Dictionary<RecordKey, NavDoorLinkFact>());
}

/// <summary>
/// Collects, while the load order is walked highest priority first, the navmesh versions of the target spaces and the navmesh links
/// of their doors, then judges the navmeshes the target plugin overrides, deletes or adds (<see cref="NavmeshRecordFact"/>).
/// Memory stays bounded: every navmesh keeps only its winner and its version just below the target plugin's, and only the
/// navmeshes the target plugin changed keep the versions in between.
/// </summary>
internal sealed class NavmeshVersionCollector(PluginName target, TargetFootprint footprint, PluginNames names)
{
    private readonly TargetOverrideWalk<NavmeshVersion?, RecordKey> _walk = new(target);
    private readonly Dictionary<RecordKey, (PluginName Plugin, NavmeshVersion Version)> _winners = new();
    private readonly Dictionary<RecordKey, (PluginName Plugin, NavmeshVersion Version)> _earlier = new();
    private readonly Dictionary<RecordKey, NavDoorLinkFact> _winningDoorLinks = new();
    private readonly Dictionary<RecordKey, NavDoorLinkFact> _earlierDoorLinks = new();

    /// <summary>The cell whose child list holds the winning version of each placed record the target plugin overrides.</summary>
    private readonly Dictionary<RecordKey, RecordKey> _winningDoorCells = new();

    /// <summary>Doors whose winner loads at or after the target plugin and whose version below it is not read yet.</summary>
    private readonly HashSet<RecordKey> _doorsAwaitingEarlierVersion = new();

    private readonly Dictionary<RecordKey, PartOutcome> _doorLinkOutcomes = new();

    /// <summary>The cells of the target spaces that some plugin's version deletes.</summary>
    private readonly HashSet<RecordKey> _deletedCells = new();

    /// <param name="belowTarget">The plugin loads before the target plugin.</param>
    /// <remarks>Like placed records, a plugin's first copy of a navmesh found is the one kept.</remarks>
    public void SeeNavmeshes(ICellGetter cell, RecordKey cellKey, RecordKey spaceKey, PluginName plugin, bool belowTarget)
    {
        if (cell.IsDeleted) _deletedCells.Add(cellKey);
        var place = CellPlaces.PlaceOf(cell, cellKey, spaceKey);
        foreach (var navmesh in cell.NavigationMeshes)
        {
            var key = names.KeyOf(navmesh.FormKey);
            var version = new NavmeshVersion(navmesh, cellKey, place);
            if (footprint.ChangesNavmesh(key)) _walk.Keep(key, plugin, version, spaceKey);
            _winners.TryAdd(key, (plugin, version));
            if (belowTarget) _earlier.TryAdd(key, (plugin, version));
        }
    }

    /// <param name="cell">The cell whose child list holds the version.</param>
    /// <param name="isWinner">The first version of the record found.</param>
    /// <param name="belowTarget">The version's plugin loads before the target plugin.</param>
    public void SeeDoorVersion(RecordKey key, IPlacedGetter record, RecordKey cell, bool isWinner, bool belowTarget)
    {
        if (record is not IPlacedObjectGetter placed) return;
        var link = placed.NavigationDoorLink is { } doorLink ? new NavDoorLinkFact(names.KeyOf(doorLink.NavMesh.FormKey), doorLink.TeleportMarkerTriangle) : (NavDoorLinkFact?)null;
        if (isWinner)
        {
            if (footprint.OverriddenRecords.Contains(key)) _winningDoorCells.Add(key, cell);
            if (link is { } winning) _winningDoorLinks.Add(key, winning);
            if (!belowTarget)
            {
                _doorsAwaitingEarlierVersion.Add(key);
                return;
            }
        }
        else if (!belowTarget || !_doorsAwaitingEarlierVersion.Remove(key)) return;
        if (link is { } earlier) _earlierDoorLinks.Add(key, earlier);
    }

    /// <summary>What the target plugin last changed in the navmesh link of a door it overrides.</summary>
    public void KeepDoorLinkOutcome(RecordKey door, PartOutcome outcome)
    {
        if (outcome is not PartOutcome.Unchanged) _doorLinkOutcomes.Add(door, outcome);
    }

    /// <param name="patch">The patch, whose version holds what earlier patchers of the group wrote.</param>
    /// <returns>
    /// The navmeshes with a part that is not <see cref="PartOutcome.Unchanged"/>, in key order; and the navmesh link of each placed
    /// record the target plugin overrides that has one in its version below the target plugin's or its winning version, by key.
    /// </returns>
    public (ImmutableArray<NavmeshRecordFact> Facts, NavmeshRecordHandles Handles, IReadOnlyDictionary<RecordKey, DoorLinkFact> OverriddenDoorLinks) Judge(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, PluginName patch)
    {
        var changed = ChangedNavmeshVersions();
        var linkingInto = NavmeshesLinkingInto(changed.Keys.ToHashSet());
        var neighbours = changed.ToDictionary(entry => entry.Key, entry => NeighboursOf(entry.Key, entry.Value.Versions, linkingInto));
        var maps = NavmeshMaps.Read(loadOrder, changed.Keys.Concat(neighbours.Values.SelectMany(keys => keys)).ToHashSet(), target, names);
        var judging = new Judging(maps, patch, DoorsNamingEachNavmesh(), new Dictionary<RecordKey, NeighbourNavmesh>());
        var judged = changed
            .OrderBy(entry => entry.Key, RecordKeyOrder.Comparer)
            .Select(entry => JudgeNavmesh(entry.Key, entry.Value.SpaceKey, entry.Value.Versions, neighbours[entry.Key], judging))
            .ToList();
        ImmutableArray<NavmeshRecordFact> facts = [.. WithReplacements(judged).Where(IsChange)];
        var handles = new NavmeshRecordHandles(
            facts.ToDictionary(fact => fact.Key, fact => changed[fact.Key].Versions),
            maps.Versions,
            _winners.ToDictionary(entry => entry.Key, entry => entry.Value.Version),
            _winningDoorLinks);
        return (facts, handles, OverriddenDoorLinks());
    }

    private Dictionary<RecordKey, DoorLinkFact> OverriddenDoorLinks() =>
        _winningDoorLinks.Keys.Concat(_earlierDoorLinks.Keys)
            .Where(footprint.OverriddenRecords.Contains)
            .Distinct()
            .ToDictionary(door => door, DoorLinkOf);

    private DoorLinkFact DoorLinkOf(RecordKey door) => new(
        door,
        _doorLinkOutcomes.GetValueOrDefault(door) ?? new PartOutcome.Unchanged(),
        _earlierDoorLinks.TryGetValue(door, out var earlier) ? earlier : null,
        _winningDoorLinks.TryGetValue(door, out var current) ? current : null,
        _winningDoorCells.TryGetValue(door, out var cell) && _deletedCells.Contains(cell));

    private Dictionary<RecordKey, (RecordKey SpaceKey, RecordVersions<NavmeshVersion?> Versions)> ChangedNavmeshVersions()
    {
        var changed = _walk.Read.ToDictionary(entry => entry.Key, entry => (entry.Place, entry.Versions));
        foreach (var (key, spaceKey, plugins, versions) in _walk.DefinedByTarget)
        {
            // No record below the target plugin's version; PartHistory names only the plugins above it.
            changed.Add(key, (spaceKey, new RecordVersions<NavmeshVersion?>([target, .. plugins], [null, .. versions])));
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

    /// <summary>The navmeshes of the target spaces linked to the navmesh by its version below the target plugin's, by the target plugin's, or by their winner.</summary>
    private ImmutableArray<RecordKey> NeighboursOf(RecordKey key, RecordVersions<NavmeshVersion?> read, Dictionary<RecordKey, List<RecordKey>> linkingInto)
    {
        var linked = new HashSet<RecordKey>(linkingInto.GetValueOrDefault(key) ?? []);
        foreach (var version in new[] { read.Earlier, read.Versions[PartHistory.TargetVersionIndex] })
        {
            if (version is not null) linked.UnionWith(NavmeshShapes.LinkedNavmeshes(version.Record, names));
        }
        linked.Remove(key);
        return [.. linked.Where(_winners.ContainsKey).Order(RecordKeyOrder.Comparer)];
    }

    /// <summary>For each navmesh, the doors whose winning link or link below the target plugin names it.</summary>
    private Dictionary<RecordKey, List<RecordKey>> DoorsNamingEachNavmesh()
    {
        var doors = new Dictionary<RecordKey, List<RecordKey>>();
        foreach (var (door, link) in _winningDoorLinks.Concat(_earlierDoorLinks)) KeyedGroups.GetOrAddList(doors, link.Navmesh).Add(door);
        return doors;
    }

    /// <remarks>When a version above the target plugin's deletes a navmesh the target plugin keeps, nothing in it goes back.</remarks>
    private NavmeshRecordFact JudgeNavmesh(RecordKey key, RecordKey spaceKey, RecordVersions<NavmeshVersion?> read, ImmutableArray<RecordKey> neighbours, Judging judging)
    {
        var own = read.Versions[PartHistory.TargetVersionIndex]!;
        var (geometry, presence) = NavmeshPartHistory.Of(read, judging.Patch);
        var mapEntry = judging.Maps.EntryOutcome(key, judging.Patch);
        if (IsDeletedAboveTarget(read, own)) (geometry, presence, mapEntry) = (NothingGoesBack(geometry), NothingGoesBack(presence), NothingGoesBack(mapEntry));

        var earlier = read.Earlier is { } below ? NavmeshShapes.Read(below, read.Plugins[0], judging.Maps.EntryBelowTarget(key), names) : null;
        var ownEntry = judging.Maps.EntryUpToTarget(key);
        var current = own.Record.IsDeleted && earlier is not null
            ? earlier with { Plugin = target, Cell = own.Cell, IsDeleted = true, MapEntry = ownEntry }
            : NavmeshShapes.Read(own, target, ownEntry, names);
        return new NavmeshRecordFact(
            key,
            spaceKey,
            read.Winner!.Cell,
            read.WinningMod,
            CaseOf(key),
            geometry,
            presence,
            mapEntry,
            earlier,
            current,
            [],
            CanGoBack(geometry, presence, mapEntry) ? [.. neighbours.Select(neighbour => NeighbourOf(neighbour, judging))] : [],
            DoorsOf(key, [earlier, current], judging),
            judging.Maps.WinningPreferredPaths.Contains(key),
            judging.Maps.WinningMapMod,
            _deletedCells.Contains(read.Winner!.Cell),
            read.WinningMod == target ? null : NavmeshShapes.Read(read.Winner!, read.WinningMod, judging.Maps.WinningEntry(key), names),
            judging.Maps.WinningEntry(key));
    }

    private NavmeshCase CaseOf(RecordKey key) =>
        footprint.AddedNavmeshes.Contains(key) ? NavmeshCase.Added
        : footprint.DeletedNavmeshes.Contains(key) ? NavmeshCase.DeletedWithoutReplacement
        : NavmeshCase.Edited;

    private static bool IsDeletedAboveTarget(RecordVersions<NavmeshVersion?> read, NavmeshVersion own) =>
        !own.Record.IsDeleted && read.Versions.Skip(PartHistory.TargetVersionIndex + 1).Any(version => version!.Record.IsDeleted);

    /// <summary>A navmesh with a part a later plugin changed again never goes back, so no neighbour's shape is needed for it.</summary>
    private static bool CanGoBack(PartOutcome geometry, PartOutcome presence, PartOutcome mapEntry) =>
        geometry is not PartOutcome.LeftToLaterPlugin && presence is not PartOutcome.LeftToLaterPlugin && mapEntry is not PartOutcome.LeftToLaterPlugin;

    private static PartOutcome NothingGoesBack(PartOutcome outcome) =>
        outcome is PartOutcome.Undo or PartOutcome.KeptForRecordFormat ? new PartOutcome.Unchanged() : outcome;

    private NeighbourNavmesh NeighbourOf(RecordKey neighbour, Judging judging)
    {
        if (judging.Neighbours.TryGetValue(neighbour, out var known)) return known;
        var (winningPlugin, winner) = _winners[neighbour];
        var read = new NeighbourNavmesh(
            neighbour,
            NavmeshShapes.Read(winner, winningPlugin, judging.Maps.WinningEntry(neighbour), names),
            _earlier.TryGetValue(neighbour, out var earlier) ? NavmeshShapes.Read(earlier.Version, earlier.Plugin, judging.Maps.EntryBelowTarget(neighbour), names) : null);
        judging.Neighbours.Add(neighbour, read);
        return read;
    }

    /// <summary>The doors the navmesh's door triangles or map entries name, and the doors whose navmesh link names it.</summary>
    private ImmutableArray<DoorLinkFact> DoorsOf(RecordKey key, NavmeshShape?[] shapes, Judging judging)
    {
        var doors = new HashSet<RecordKey>(judging.DoorsNaming.GetValueOrDefault(key) ?? []);
        foreach (var shape in shapes.OfType<NavmeshShape>())
        {
            doors.UnionWith(shape.DoorTriangles.Select(door => door.Door));
            doors.UnionWith(shape.MapEntry?.LinkedDoors ?? []);
        }
        doors.ExceptWith([RecordKey.Null, RecordKey.None]);
        return [.. doors.Order(RecordKeyOrder.Comparer).Select(DoorLinkOf)];
    }

    /// <summary>A deleted navmesh is replaced by the navmeshes the target plugin adds in its cell that overlap its earlier triangles from above.</summary>
    private static IEnumerable<NavmeshRecordFact> WithReplacements(List<NavmeshRecordFact> facts)
    {
        var added = facts.Where(fact => fact.Case == NavmeshCase.Added).ToList();
        foreach (var fact in facts)
        {
            if (fact is { Case: NavmeshCase.DeletedWithoutReplacement, Earlier: { } earlier }
                && added.Where(navmesh => navmesh.Current.Cell == earlier.Cell && OverlapFromAbove(earlier, navmesh.Current)).Select(navmesh => navmesh.Key).ToImmutableArray() is { IsEmpty: false } replacedBy)
            {
                yield return fact with { Case = NavmeshCase.DeletedAndReplaced, ReplacedBy = replacedBy };
                continue;
            }
            yield return fact;
        }
    }

    private static bool OverlapFromAbove(NavmeshShape one, NavmeshShape other)
    {
        var others = other.WorldTriangles().ToList();
        return one.WorldTriangles().Any(triangle => others.Any(otherTriangle => TriangleFootprints.Overlap(triangle, otherTriangle)));
    }

    private static bool IsChange(NavmeshRecordFact fact) =>
        fact.Geometry is not PartOutcome.Unchanged || fact.Presence is not PartOutcome.Unchanged || fact.MapEntry is not PartOutcome.Unchanged;

    /// <summary>What judging every changed navmesh shares.</summary>
    /// <param name="Neighbours">The neighbours read so far, so each is read once.</param>
    private sealed record Judging(NavmeshMaps Maps, PluginName Patch, Dictionary<RecordKey, List<RecordKey>> DoorsNaming, Dictionary<RecordKey, NeighbourNavmesh> Neighbours);
}
