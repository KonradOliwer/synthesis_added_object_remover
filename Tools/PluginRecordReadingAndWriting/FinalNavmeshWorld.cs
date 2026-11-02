using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// The navmeshes, navmesh info map and doors of the final world: the patch's records on top of the winning records of the plugin
/// spaces. Read after the writing ends; it never changes the patch. The reverse links of the winning records are shared by every
/// final world over the same read, so a world reads the links of the patch's own records only. Not thread-safe.
/// </summary>
/// <param name="synthesis">Resolves the navmeshes outside the plugin spaces that a navmesh info map names, and the winning cells.</param>
internal sealed class FinalNavmeshWorld(ISkyrimModGetter patch, RecordHandles handles, SynthesisWriteCache synthesis) : INavmeshWorldView
{
    private readonly NavmeshRecordHandles _read = handles.NavmeshRecords;
    private readonly Dictionary<RecordKey, NavmeshView?> _views = [];
    private readonly HashSet<RecordKey> _patchDeletedCells = [];
    private Dictionary<RecordKey, NavmeshVersion>? _patchNavmeshes;
    private Dictionary<RecordKey, IPlacedObjectGetter>? _patchPlaced;
    private Dictionary<RecordKey, List<RecordKey>>? _patchNavmeshesLinkingTo;
    private Dictionary<RecordKey, List<RecordKey>>? _patchDoorsLinkingTo;

    public IEnumerable<ImmutableArray<RecordKey>> PreferredPathSets
    {
        get
        {
            var map = FinalMap();
            var sets = MutagenCalls.Read(() => map?.PreferredPathing?.NavmeshSets.Select(set => set.Navmeshes.Select(navmesh => navmesh.FormKey).ToArray()).ToArray()) ?? [];
            return sets.Select(set => set.Select(navmesh => navmesh.KeyAsWritten()).ToImmutableArray());
        }
    }

    private Dictionary<RecordKey, NavmeshVersion> PatchNavmeshes => _patchNavmeshes ??= ReadPatchNavmeshes();

    private Dictionary<RecordKey, IPlacedObjectGetter> PatchPlaced =>
        _patchPlaced ??= patch.EnumerateMajorRecords<IPlacedObjectGetter>().ToDictionary(placed => placed.FormKey.KeyAsWritten());

    public NavmeshView? NavmeshOf(RecordKey navmesh)
    {
        if (_views.TryGetValue(navmesh, out var view)) return view;
        view = FinalVersionOf(navmesh) is { } version ? ViewOf(navmesh, version) : null;
        _views.Add(navmesh, view);
        return view;
    }

    public bool Exists(RecordKey navmesh) =>
        NavmeshOf(navmesh) is { } view
            ? !view.IsDeleted
            : synthesis.Cache.TryResolve<INavigationMeshGetter>(navmesh.ToFormKey(), out var record) && !record.IsDeleted;

    public NavmeshEntryView? EntryOf(RecordKey navmesh)
    {
        var key = navmesh.ToFormKey();
        List<IEnumerable<INavigationMeshInfoMapGetter>> searches =
            [patch.NavigationMeshInfoMaps, .. _read.Maps.Reverse().Select(version => (IEnumerable<INavigationMeshInfoMapGetter>)[version.Map])];
        var stored = MutagenCalls.Read<(IANavigationMapInfoParentGetter Parent, FormKey[] MergedTo, FormKey[] PreferredMerges, FormKey[] Doors)?>(() =>
        {
            var entry = searches
                .Select(maps => maps.SelectMany(map => map.MapInfos).LastOrDefault(info => info.NavigationMesh.FormKey == key))
                .FirstOrDefault(found => found is not null);
            return entry is null
                ? null
                : (entry.Parent,
                    entry.MergedTo.Select(merged => merged.FormKey).ToArray(),
                    entry.PreferredMerges.Select(merged => merged.FormKey).ToArray(),
                    entry.LinkedDoors.Select(door => door.Door.FormKey).ToArray());
        });
        return stored is not { } found
            ? null
            : new NavmeshEntryView(
                PlaceOf(found.Parent),
                [.. found.MergedTo.Select(merged => merged.KeyAsWritten())],
                [.. found.PreferredMerges.Select(merged => merged.KeyAsWritten())],
                [.. found.Doors.Select(door => door.KeyAsWritten())]);
    }

    public DoorView? DoorOf(RecordKey door)
    {
        IPlacedGetter? record = PatchPlaced.TryGetValue(door, out var written) ? written : handles.FindRecord(door);
        return record is null ? null : new DoorView(record.IsDeleted, LinkOf(record));
    }

    /// <returns>In key order: the winners that link to it but those the patch holds, and the patch's navmeshes that link to it.</returns>
    public IEnumerable<RecordKey> NavmeshesLinkingTo(RecordKey navmesh)
    {
        var patchLinking = _patchNavmeshesLinkingTo ??= WinningNavmeshLinks.IndexNavmeshesLinkingTo(PatchNavmeshes);
        return WithPatchLinks(_read.Links.NavmeshesLinkingTo(navmesh), PatchNavmeshes.ContainsKey, patchLinking, navmesh);
    }

    /// <returns>In key order: the winning doors that link to it but those the patch holds, and the patch's placed records that link to it.</returns>
    public IEnumerable<RecordKey> DoorsLinkingTo(RecordKey navmesh)
    {
        var patchLinking = _patchDoorsLinkingTo ??= WinningNavmeshLinks.IndexDoorsLinkingTo(
            PatchPlaced.Select(placed => (placed.Key, Link: LinkOf(placed.Value))).Where(placed => placed.Link is not null).Select(placed => KeyValuePair.Create(placed.Key, placed.Link!.Value)));
        return WithPatchLinks(_read.Links.DoorsLinkingTo(navmesh), PatchPlaced.ContainsKey, patchLinking, navmesh);
    }

    /// <returns>In key order: the winners that link to the navmesh but those the patch holds, and the patch's records that link to it.</returns>
    private static IEnumerable<RecordKey> WithPatchLinks(
        IEnumerable<RecordKey> winnersLinking, Func<RecordKey, bool> patchHolds, Dictionary<RecordKey, List<RecordKey>> patchLinking, RecordKey navmesh) =>
        winnersLinking.Where(winner => !patchHolds(winner)).Concat(patchLinking.GetValueOrDefault(navmesh) ?? []).Order(RecordKeyOrder.Comparer);

    private NavmeshVersion? FinalVersionOf(RecordKey navmesh) =>
        PatchNavmeshes.GetValueOrDefault(navmesh) ?? _read.Winners.GetValueOrDefault(navmesh);

    /// <summary>The patch's navmeshes, each with the cell of the patch that holds it; the patch's deleted cells are noted.</summary>
    private Dictionary<RecordKey, NavmeshVersion> ReadPatchNavmeshes()
    {
        var navmeshes = new Dictionary<RecordKey, NavmeshVersion>();
        foreach (var context in patch.EnumerateMajorRecordContexts<ICell, ICellGetter>(synthesis.Cache))
        {
            var cell = context.Record;
            var cellKey = cell.FormKey.KeyAsWritten();
            if (cell.IsDeleted) _patchDeletedCells.Add(cellKey);
            var place = CellPlaces.PlaceOf(cell, cellKey, CellPlaces.WorldspaceOf(context)?.FormKey.KeyAsWritten() ?? cellKey);
            foreach (var navmesh in cell.NavigationMeshes) navmeshes[navmesh.FormKey.KeyAsWritten()] = new NavmeshVersion(navmesh, cellKey, place);
        }
        return navmeshes;
    }

    /// <summary>A navmesh the patch holds lies in the patch's cell; any other in the winning cell.</summary>
    private bool HeldInDeletedCell(RecordKey key, NavmeshVersion version) =>
        PatchNavmeshes.ContainsKey(key)
            ? _patchDeletedCells.Contains(version.Cell)
            : synthesis.Cache.TryResolve<ICellGetter>(version.Cell.ToFormKey(), out var cell) && cell.IsDeleted;

    private NavmeshView ViewOf(RecordKey key, NavmeshVersion version)
    {
        var record = version.Record;
        var inDeletedCell = HeldInDeletedCell(key, version);
        if (MutagenCalls.Read(() => record.Data) is not { } data) return new NavmeshView(record.IsDeleted, version.Place, inDeletedCell, null, [], [], [], [], Vector3.Zero, Vector3.Zero, []);
        var (parent, vertices, triangles, edgeLinks, doorTriangles, min, max) = MutagenCalls.Read(() => (
            data.Parent,
            data.Vertices.ToArray(),
            data.Triangles.Select(triangle => (
                Corners: triangle.Vertices,
                Edges: NavmeshTriangleEdges.All.Select(edge => (IsEdgeLink: triangle.Flags.HasFlag(edge.Flag), Value: edge.Value(triangle))).ToArray())).ToArray(),
            data.EdgeLinks.Select(link => (Mesh: link.Mesh.FormKey, link.TriangleIndex)).ToArray(),
            data.DoorTriangles.Select(door => (door.TriangleBeforeDoor, Door: door.Door.FormKey)).ToArray(),
            data.Min,
            data.Max));
        return new NavmeshView(
            record.IsDeleted,
            version.Place,
            inDeletedCell,
            PlaceOf(parent),
            [.. vertices.Select(ToVector)],
            [.. triangles.Select(TriangleOf)],
            [.. edgeLinks.Select(link => new NavmeshLinkTarget(link.Mesh.KeyAsWritten(), link.TriangleIndex))],
            [.. doorTriangles.Select(door => new NavmeshDoorTriangleFact(door.TriangleBeforeDoor, door.Door.KeyAsWritten()))],
            ToVector(min),
            ToVector(max),
            GridSourceTriangleCounts(key, data, triangles.Length));
    }

    /// <summary>
    /// A navmesh the patch holds takes its grid from one of the versions read; any other navmesh is the version read, whose grid is
    /// its own.
    /// </summary>
    private ImmutableArray<int> GridSourceTriangleCounts(RecordKey key, INavigationMeshDataGetter data, int triangleCount)
    {
        if (!PatchNavmeshes.ContainsKey(key)) return [triangleCount];
        var changed = _read.Navmeshes.TryGetValue(key, out var versions) ? versions.Versions : [];
        var sources = changed.Append(_read.Winners.GetValueOrDefault(key)).Select(version => version?.Record).OfType<INavigationMeshGetter>().ToList();
        return
        [
            .. MutagenCalls.Read(() => sources
                .Select(source => source.Data)
                .OfType<INavigationMeshDataGetter>()
                .Where(source => source.NavmeshGridDivisor == data.NavmeshGridDivisor && source.NavmeshGrid.Span.SequenceEqual(data.NavmeshGrid.Span))
                .Select(source => source.Triangles.Count)
                .Distinct()
                .ToList()),
        ];
    }

    private static NavmeshTriangleView TriangleOf((P3Int16 Corners, (bool IsEdgeLink, short Value)[] Edges) triangle) => new(
        [triangle.Corners.X, triangle.Corners.Y, triangle.Corners.Z],
        [.. triangle.Edges.Select(edge => new NavmeshEdgeView(edge.IsEdgeLink, edge.Value == NavmeshTriangleEdges.None ? null : edge.Value))]);

    private static NavDoorLinkFact? LinkOf(IPlacedGetter record)
    {
        if (record is not IPlacedObjectGetter placed) return null;
        var link = MutagenCalls.Read(() => placed.NavigationDoorLink is { } stored ? (Navmesh: stored.NavMesh.FormKey, Triangle: (int)stored.TeleportMarkerTriangle) : ((FormKey Navmesh, int Triangle)?)null);
        return link is { } found ? new NavDoorLinkFact(found.Navmesh.KeyAsWritten(), found.Triangle) : null;
    }

    /// <summary>The patch's map when it holds one, which carries the winning map's preferred paths; otherwise the last plugin's.</summary>
    private INavigationMeshInfoMapGetter? FinalMap() => patch.NavigationMeshInfoMaps.FirstOrDefault() ?? _read.Maps.LastOrDefault()?.Map;

    private static NavmeshPlace PlaceOf(IANavmeshParentGetter parent) =>
        PlaceOf<IWorldspaceNavmeshParentGetter, ICellNavmeshParentGetter>(parent, ReadWorldParent, ReadCellParent);

    private static NavmeshPlace PlaceOf(IANavigationMapInfoParentGetter parent) =>
        PlaceOf<INavigationMapInfoWorldParentGetter, INavigationMapInfoCellParentGetter>(parent, ReadMapWorldParent, ReadMapCellParent);

    private static (FormKey Worldspace, P2Int16 Coordinates) ReadWorldParent(IWorldspaceNavmeshParentGetter world) =>
        MutagenCalls.Read(() => (world.Parent.FormKey, world.Coordinates));

    private static FormKey ReadCellParent(ICellNavmeshParentGetter cell) => MutagenCalls.Read(() => cell.Parent.FormKey);

    private static (FormKey Worldspace, P2Int16 Coordinates) ReadMapWorldParent(INavigationMapInfoWorldParentGetter world) =>
        MutagenCalls.Read(() => (world.ParentWorldspace.FormKey, world.ParentWorldspaceCoord));

    private static FormKey ReadMapCellParent(INavigationMapInfoCellParentGetter cell) => MutagenCalls.Read(() => cell.ParentCell.FormKey);

    /// <summary>Mutagen's navmesh parent families share no base, so each arm is told which of its own getters to read.</summary>
    private static NavmeshPlace PlaceOf<TWorldParent, TCellParent>(
        object parent, Func<TWorldParent, (FormKey Worldspace, P2Int16 Coordinates)> readWorldParent, Func<TCellParent, FormKey> readCellParent) => parent switch
    {
        TWorldParent world => WorldPlace(readWorldParent(world)),
        TCellParent cell => CellPlace(readCellParent(cell)),
        _ => throw new InvalidOperationException($"A navmesh parent of kind {parent.GetType().Name} is not read."),
    };

    private static NavmeshPlace WorldPlace((FormKey Worldspace, P2Int16 Coordinates) parent) =>
        new(parent.Worldspace.KeyAsWritten(), NavmeshParentCoordinates.GridPointOf(parent.Coordinates));

    private static NavmeshPlace CellPlace(FormKey cell) => new(cell.KeyAsWritten(), null);

    private static Vector3 ToVector(P3Float point) => new(point.X, point.Y, point.Z);
}
