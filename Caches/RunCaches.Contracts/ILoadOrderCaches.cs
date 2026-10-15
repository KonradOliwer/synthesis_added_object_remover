using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>The caches that exist from the start of the run, before the placed objects are collected.</summary>
public interface ILoadOrderCaches
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

    /// <summary>How the bounds of the base objects asked for so far were found, counted from C2 to C5.</summary>
    BoundsStats CountBounds();

    /// <summary>Meshes of fog, light rays and the like among the meshes read so far.</summary>
    int CountEffectOnlyMeshes();

    /// <summary>Distinct sets of body meshes measured so far (C7).</summary>
    int CountBodyMeshSets();
}
