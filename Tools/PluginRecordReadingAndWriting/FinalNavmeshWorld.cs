using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// The navmeshes, navmesh info map and doors of the final world: the patch's records on top of the winning records of the target
/// spaces. Read after the writing ends; it never changes the patch. The reverse links of the winning records are shared by every
/// final world over the same read, so a world reads the links of the patch's own records only. Not thread-safe.
/// </summary>
/// <param name="synthesis">Resolves the navmeshes outside the target spaces that a navmesh info map names, and the winning cells.</param>
internal sealed class FinalNavmeshWorld(ISkyrimModGetter patch, RecordHandles handles, SynthesisWriteCache synthesis) : INavmeshWorldView
{
    private readonly NavmeshRecordHandles _read = handles.NavmeshRecords;
    private readonly Dictionary<RecordKey, NavmeshView?> _views = [];
    private readonly HashSet<RecordKey> _patchDeletedCells = [];
    private Dictionary<RecordKey, NavmeshVersion>? _patchNavmeshes;
    private Dictionary<RecordKey, IPlacedObjectGetter>? _patchPlaced;
    private Dictionary<RecordKey, List<RecordKey>>? _patchNavmeshesLinkingTo;
    private Dictionary<RecordKey, List<RecordKey>>? _patchDoorsLinkingTo;

    public IEnumerable<ImmutableArray<RecordKey>> PreferredPathSets =>
        FinalMap()?.PreferredPathing?.NavmeshSets.Select(set => set.Navmeshes.Select(navmesh => navmesh.FormKey.KeyAsWritten()).ToImmutableArray()) ?? [];

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
        var entry = LastEntryIn(patch.NavigationMeshInfoMaps, key)
            ?? _read.Maps.Reverse().Select(version => LastEntryIn([version.Map], key)).FirstOrDefault(found => found is not null);
        return entry is null
            ? null
            : new NavmeshEntryView(
                PlaceOf(entry.Parent),
                [.. entry.MergedTo.Select(merged => merged.FormKey.KeyAsWritten())],
                [.. entry.PreferredMerges.Select(merged => merged.FormKey.KeyAsWritten())],
                [.. entry.LinkedDoors.Select(door => door.Door.FormKey.KeyAsWritten())]);
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
        return _read.Links.NavmeshesLinkingTo(navmesh).Where(winner => !PatchNavmeshes.ContainsKey(winner))
            .Concat(patchLinking.GetValueOrDefault(navmesh) ?? [])
            .Order(RecordKeyOrder.Comparer);
    }

    /// <returns>In key order: the winning doors that link to it but those the patch holds, and the patch's placed records that link to it.</returns>
    public IEnumerable<RecordKey> DoorsLinkingTo(RecordKey navmesh)
    {
        var patchLinking = _patchDoorsLinkingTo ??= WinningNavmeshLinks.IndexDoorsLinkingTo(
            PatchPlaced.Select(placed => (placed.Key, Link: LinkOf(placed.Value))).Where(placed => placed.Link is not null).Select(placed => KeyValuePair.Create(placed.Key, placed.Link!.Value)));
        return _read.Links.DoorsLinkingTo(navmesh).Where(door => !PatchPlaced.ContainsKey(door))
            .Concat(patchLinking.GetValueOrDefault(navmesh) ?? [])
            .Order(RecordKeyOrder.Comparer);
    }

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
        if (record.Data is not { } data) return new NavmeshView(record.IsDeleted, version.Place, inDeletedCell, null, [], [], [], [], Vector3.Zero, Vector3.Zero, []);
        return new NavmeshView(
            record.IsDeleted,
            version.Place,
            inDeletedCell,
            PlaceOf(data.Parent),
            [.. data.Vertices.Select(ToVector)],
            [.. data.Triangles.Select(TriangleOf)],
            [.. data.EdgeLinks.Select(link => new NavmeshLinkTarget(link.Mesh.FormKey.KeyAsWritten(), link.TriangleIndex))],
            [.. data.DoorTriangles.Select(door => new NavmeshDoorTriangleFact(door.TriangleBeforeDoor, door.Door.FormKey.KeyAsWritten()))],
            ToVector(data.Min),
            ToVector(data.Max),
            GridSourceTriangleCounts(key, data));
    }

    /// <summary>
    /// A navmesh the patch holds takes its grid from one of the versions read; any other navmesh is the version read, whose grid is
    /// its own.
    /// </summary>
    private ImmutableArray<int> GridSourceTriangleCounts(RecordKey key, INavigationMeshDataGetter data)
    {
        if (!PatchNavmeshes.ContainsKey(key)) return [data.Triangles.Count];
        var changed = _read.Navmeshes.TryGetValue(key, out var versions) ? versions.Versions : [];
        return
        [
            .. changed.Append(_read.Winners.GetValueOrDefault(key))
                .Select(version => version?.Record.Data)
                .OfType<INavigationMeshDataGetter>()
                .Where(source => source.NavmeshGridDivisor == data.NavmeshGridDivisor && source.NavmeshGrid.Span.SequenceEqual(data.NavmeshGrid.Span))
                .Select(source => source.Triangles.Count)
                .Distinct(),
        ];
    }

    private static NavmeshTriangleView TriangleOf(INavmeshTriangleGetter triangle) => new(
        [triangle.Vertices.X, triangle.Vertices.Y, triangle.Vertices.Z],
        [.. NavmeshTriangleEdges.All.Select(edge => new NavmeshEdgeView(triangle.Flags.HasFlag(edge.Flag), edge.Value(triangle) is var value && value == NavmeshTriangleEdges.None ? null : value))]);

    private static NavDoorLinkFact? LinkOf(IPlacedGetter record) =>
        record is IPlacedObjectGetter { NavigationDoorLink: { } link } ? new NavDoorLinkFact(link.NavMesh.FormKey.KeyAsWritten(), link.TeleportMarkerTriangle) : null;

    /// <summary>The patch's map when it holds one, which carries the winning map's preferred paths; otherwise the last plugin's.</summary>
    private INavigationMeshInfoMapGetter? FinalMap() => patch.NavigationMeshInfoMaps.FirstOrDefault() ?? _read.Maps.LastOrDefault()?.Map;

    private static INavigationMapInfoGetter? LastEntryIn(IEnumerable<INavigationMeshInfoMapGetter> maps, FormKey navmesh) =>
        maps.SelectMany(map => map.MapInfos).LastOrDefault(entry => entry.NavigationMesh.FormKey == navmesh);

    private static NavmeshPlace PlaceOf(IANavmeshParentGetter parent) => parent switch
    {
        IWorldspaceNavmeshParentGetter world => new NavmeshPlace(world.Parent.FormKey.KeyAsWritten(), NavmeshParentCoordinates.GridPointOf(world.Coordinates)),
        ICellNavmeshParentGetter cell => new NavmeshPlace(cell.Parent.FormKey.KeyAsWritten(), null),
        _ => throw new InvalidOperationException($"A navmesh parent of kind {parent.GetType().Name} is not read."),
    };

    private static NavmeshPlace PlaceOf(IANavigationMapInfoParentGetter parent) => parent switch
    {
        INavigationMapInfoWorldParentGetter world => new NavmeshPlace(world.ParentWorldspace.FormKey.KeyAsWritten(), NavmeshParentCoordinates.GridPointOf(world.ParentWorldspaceCoord)),
        INavigationMapInfoCellParentGetter cell => new NavmeshPlace(cell.ParentCell.FormKey.KeyAsWritten(), null),
        _ => throw new InvalidOperationException($"A navmesh info map entry's parent of kind {parent.GetType().Name} is not read."),
    };

    private static Vector3 ToVector(P3Float point) => new(point.X, point.Y, point.Z);
}
