using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Writes navmeshes the read's plugin edited, deleted or added into the patch, in the patch's overrides of the cells that hold them.
/// Not thread-safe.
/// </summary>
internal sealed class NavmeshWriter(CellOverrides cells, RecordHandles handles)
{
    private const NavmeshTriangle.Flag PathFlags = NavmeshTriangle.Flag.Preferred | NavmeshTriangle.Flag.Door;

    private readonly Dictionary<FormKey, INavigationMesh> _navmeshes = [];

    /// <summary>The winner's override takes the earlier version's geometry through the part table it was compared with; its parent stays.</summary>
    public void CopyEarlierGeometry(RecordKey navmesh)
    {
        var versions = handles.NavmeshOf(navmesh);
        var earlier = Present(versions.Earlier, navmesh);
        var target = GetOrAddOverride(Present(versions.Winner, navmesh));
        if (!RawByteFormat.AllowsCopy(NavmeshRecordParts.Geometry, earlier.Record, target))
            throw new InvalidOperationException($"The geometry of navmesh {navmesh} holds bytes of another record format and cannot go back.");
        var parent = target.Data?.Parent.DeepCopy();
        NavmeshRecordParts.Geometry.CopyInto(target, earlier.Record);
        if ((parent, target.Data) is ({ } kept, { } data)) data.Parent = kept;
    }

    /// <remarks>
    /// The copy keeps the earlier version's FormVersion, so its raw bytes are written in the record format they follow, and its
    /// Deleted flag.
    /// </remarks>
    public void CopyEarlier(RecordKey navmesh)
    {
        var earlier = Present(handles.NavmeshOf(navmesh).Earlier, navmesh);
        var copy = (NavigationMesh)earlier.Record.DeepCopy();
        var list = cells.GetOrAdd(earlier.Cell.ToFormKey()).NavigationMeshes;
        list.RemoveAll(existing => existing.FormKey == copy.FormKey);
        list.Add(copy);
        _navmeshes[copy.FormKey] = copy;
    }

    /// <summary>Sets every vertex of the navmesh to the height and removes every edge link, door, cover and preferred triangle.</summary>
    /// <remarks>
    /// The triangles keep their count, order and vertices, and the inner edges stay, so the grid, written unchanged, still lists
    /// valid triangles.
    /// </remarks>
    public void FlattenAndDetach(RecordKey navmesh, float z)
    {
        var target = GetOrAddOverride(Present(handles.NavmeshOf(navmesh).Winner, navmesh));
        var data = target.Data ?? throw new InvalidOperationException($"Navmesh {navmesh} has no navmesh data to set the height of.");
        LowerTo(data, z);
        CutOff(data);
    }

    private static void LowerTo(INavigationMeshData data, float z)
    {
        for (var index = 0; index < data.Vertices.Count; index++) data.Vertices[index] = data.Vertices[index].WithZ(z);
        data.Min = data.Min.WithZ(z);
        data.Max = data.Max.WithZ(z);
    }

    private static void CutOff(INavigationMeshData data)
    {
        foreach (var triangle in data.Triangles)
        {
            foreach (var edge in NavmeshTriangleEdges.All.Where(edge => triangle.Flags.HasFlag(edge.Flag)))
            {
                edge.SetValue(triangle, NavmeshTriangleEdges.None);
                triangle.Flags &= ~edge.Flag;
            }
            triangle.Flags &= ~PathFlags;
            triangle.CoverFlags = 0;
            triangle.IsCover = false;
        }
        data.EdgeLinks.Clear();
        data.DoorTriangles.Clear();
    }

    private INavigationMesh GetOrAddOverride(NavmeshVersion winner)
    {
        var key = winner.Record.FormKey;
        if (_navmeshes.TryGetValue(key, out var held)) return held;
        var list = cells.GetOrAdd(winner.Cell.ToFormKey()).NavigationMeshes;
        var navmesh = list.FirstOrDefault(existing => existing.FormKey == key);
        if (navmesh is null)
        {
            navmesh = (NavigationMesh)winner.Record.DeepCopy();
            list.Add(navmesh);
        }
        _navmeshes.Add(key, navmesh);
        return navmesh;
    }

    private static NavmeshVersion Present(NavmeshVersion? version, RecordKey navmesh) =>
        version ?? throw new InvalidOperationException($"Navmesh {navmesh} has no such version to write from.");
}
