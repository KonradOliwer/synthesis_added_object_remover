using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>One version of a navmesh record, the cell record whose navmesh list holds it and where that cell lies.</summary>
internal sealed record NavmeshVersion(INavigationMeshGetter Record, RecordKey Cell, NavmeshPlace Place);

/// <summary>Reads navmesh versions and map entries as facts.</summary>
internal static class NavmeshShapes
{
    /// <param name="plugin">The plugin of the version.</param>
    /// <param name="entry">The map entry in force at the version's level.</param>
    /// <remarks>A version without navmesh data (a deleted one may have none) has no vertices or triangles.</remarks>
    public static NavmeshShape Read(NavmeshVersion version, PluginName plugin, NavmeshMapEntryFact? entry, PluginNames names)
    {
        var record = version.Record;
        if (record.Data is not { } data) return new NavmeshShape(plugin, version.Cell, record.IsDeleted, [], [], [], [], entry);

        ImmutableArray<Vector3> vertices = [.. data.Vertices.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z))];
        return new NavmeshShape(
            plugin,
            version.Cell,
            record.IsDeleted,
            vertices,
            [.. data.Triangles.Select(triangle => new NavmeshTriangleFact(
                triangle.Vertices.X, triangle.Vertices.Y, triangle.Vertices.Z, triangle.Flags.HasFlag(NavmeshTriangle.Flag.Preferred)))],
            [.. EdgeLinksOf(data, vertices, names)],
            [.. data.DoorTriangles.Select(door => new NavmeshDoorTriangleFact(door.TriangleBeforeDoor, names.KeyOf(door.Door.FormKey)))],
            entry);
    }

    public static NavmeshMapEntryFact ReadEntry(PluginName plugin, RecordKey map, INavigationMapInfoGetter entry, PluginNames names) => new(
        plugin,
        map,
        [.. entry.MergedTo.Select(navmesh => names.KeyOf(navmesh.FormKey))],
        [.. entry.PreferredMerges.Select(navmesh => names.KeyOf(navmesh.FormKey))],
        [.. entry.LinkedDoors.Select(door => names.KeyOf(door.Door.FormKey))]);

    /// <summary>The navmeshes the version's edge-link table names, whether a triangle uses the entry or not.</summary>
    public static IEnumerable<RecordKey> LinkedNavmeshes(INavigationMeshGetter navmesh, PluginNames names) =>
        navmesh.Data?.EdgeLinks.Select(link => names.KeyOf(link.Mesh.FormKey)) ?? [];

    private static IEnumerable<NavmeshEdgeLinkFact> EdgeLinksOf(INavigationMeshDataGetter data, ImmutableArray<Vector3> vertices, PluginNames names)
    {
        for (var index = 0; index < data.Triangles.Count; index++)
        {
            var triangle = data.Triangles[index];
            foreach (var edge in NavmeshTriangleEdges.All)
            {
                var value = edge.Value(triangle);
                var (from, to) = (edge.From(triangle.Vertices), edge.To(triangle.Vertices));
                if (!triangle.Flags.HasFlag(edge.Flag) || !IsIndex(value, data.EdgeLinks.Count) || !IsIndex(from, vertices.Length) || !IsIndex(to, vertices.Length)) continue;
                var link = data.EdgeLinks[value];
                yield return new NavmeshEdgeLinkFact(index, names.KeyOf(link.Mesh.FormKey), link.TriangleIndex, vertices[from], vertices[to]);
            }
        }
    }

    private static bool IsIndex(int value, int count) => value >= 0 && value < count;
}
