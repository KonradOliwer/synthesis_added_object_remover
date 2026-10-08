using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>
/// The caches of one run, listed by their interfaces; each step receives only the ones it needs.
/// Caches belong to the run that created them and are never static.
/// <list type="bullet">
/// <item>A keyed cache builds each value once per key, because its builder may record problems or counts as a side effect; only a small pure value that depends on nothing but its key may be computed twice, the first result then being kept.</item>
/// <item>A one-piece cache is computed once, on the first ask.</item>
/// <item>There are no permanent slots. Only evictable values (triangle trees) are evicted, and what the statistics count lives in a cache that is never evicted.</item>
/// <item>Statistics are neutral counts taken from the cache contents, so they do not depend on thread count or timing.</item>
/// </list>
/// The load-order caches (C2 to C8, C13, C14) exist from the start of the run; the object caches
/// (C9 to C12, <see cref="IObjectCaches"/>) exist once the placed objects are collected.
/// </summary>
public interface IRunCaches : IObjectCaches
{
    /// <summary>C2: the facts of each base object.</summary>
    IBaseFacts Bases { get; }

    /// <summary>C3: the archive index, and C4: the bounds of each mesh (<see cref="IMeshFiles.Bounds"/>).</summary>
    IMeshFiles MeshFiles { get; }

    /// <summary>C5: the shape, size and kind of each base object.</summary>
    IBaseObjectShapes Shapes { get; }

    /// <summary>C6: the triangle tree of each mesh.</summary>
    ITriangleMeshes Triangles { get; }

    /// <summary>C7: the bounds of each set of body meshes.</summary>
    IBodyMeshBounds BodyBounds { get; }

    /// <summary>C8: the bodies of NPCs.</summary>
    INpcBodies Bodies { get; }

    /// <summary>C13: the terrain heights of each exterior cell.</summary>
    ITerrainHeights Terrain { get; }

    /// <summary>C14: the navmesh triangles of each navmesh bucket.</summary>
    INavmeshes Navmeshes { get; }
}
