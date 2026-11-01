using System.Globalization;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// What a patch holds besides its placed records' positions, one line each: the sizes and door links of its placed objects, the cells'
/// settings, five rows of each land record's heights, the navmeshes and the navmesh info maps. In key order.
/// </summary>
internal static class AreaPatchDump
{
    private const int Side = TerrainGrid.VerticesPerSide;
    private const string HeightFormat = "F0";
    private const string None = "none";

    /// <summary>The south edge, the middle of the south half, the middle row, the middle of the north half and the north edge.</summary>
    private static readonly int[] RowsShown = [0, Side / 4, Side / 2, Side * 3 / 4, Side - 1];

    public static IReadOnlyList<string> Describe(ISkyrimModGetter patch)
    {
        var cells = patch.EnumerateMajorRecords<ICellGetter>().OrderBy(cell => cell.FormKey.ToString(), StringComparer.Ordinal).ToList();
        var placedObjects = cells.SelectMany(cell => cell.EnumeratePlaced()).Select(entry => entry.Record).OfType<IPlacedObjectGetter>()
            .OrderBy(placed => placed.FormKey.ToString(), StringComparer.Ordinal).ToList();
        return
        [
            .. cells.Select(DescribeCell),
            .. placedObjects.Where(placed => placed.Scale is not null).Select(placed => $"scale {placed.FormKey}: {Format(placed.Scale)}"),
            .. placedObjects.Where(placed => placed.NavigationDoorLink is not null)
                .Select(placed => $"door link {placed.FormKey}: navmesh {placed.NavigationDoorLink!.NavMesh.FormKey}, triangle {placed.NavigationDoorLink.TeleportMarkerTriangle}"),
            .. cells.Where(cell => cell.Landscape is not null).SelectMany(cell => DescribeLand(cell.Landscape!)),
            .. patch.EnumerateMajorRecords<INavigationMeshGetter>().OrderBy(navmesh => navmesh.FormKey.ToString(), StringComparer.Ordinal).Select(DescribeNavmesh),
            .. patch.NavigationMeshInfoMaps.OrderBy(map => map.FormKey.ToString(), StringComparer.Ordinal).Select(DescribeMap),
        ];
    }

    private static string DescribeCell(ICellGetter cell) =>
        $"cell {cell.FormKey}: music {cell.Music.FormKeyNullable?.ToString() ?? None}, location {cell.Location.FormKeyNullable?.ToString() ?? None}, "
        + $"fog near {Format(cell.Lighting?.FogNear)}";

    private static IEnumerable<string> DescribeLand(ILandscapeGetter land)
    {
        var heights = LandHeightReader.Read(land);
        if (heights is null) return [$"land {land.FormKey}: {None}"];
        return RowsShown.Select(row => $"land {land.FormKey} row {row}: " + string.Join(" ", heights.Skip(row * Side).Take(Side).Select(height => Format(height))));
    }

    private static string DescribeNavmesh(INavigationMeshGetter navmesh)
    {
        if (navmesh.Data is not { } data) return $"navmesh {navmesh.FormKey}: deleted {navmesh.IsDeleted}";
        var links = string.Join(", ", data.EdgeLinks.Select(link => link.Mesh.FormKey.ToString()));
        return $"navmesh {navmesh.FormKey}: deleted {navmesh.IsDeleted}, vertex z {Format(data.Vertices.Min(vertex => vertex.Z))} to {Format(data.Vertices.Max(vertex => vertex.Z))}, "
            + $"links to [{links}], door triangles {data.DoorTriangles.Count}";
    }

    private static string DescribeMap(INavigationMeshInfoMapGetter map) =>
        $"navmesh info map {map.FormKey}: " + string.Join("; ", map.MapInfos.Select(entry => $"{entry.NavigationMesh.FormKey} at z {Format(entry.Point.Z)}"));

    private static string Format(float? value) => value?.ToString(HeightFormat, CultureInfo.InvariantCulture) ?? None;
}
