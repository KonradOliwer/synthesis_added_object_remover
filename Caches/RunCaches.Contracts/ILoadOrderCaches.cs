using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>The caches that exist from the start of the run, before the placed objects are collected.</summary>
public interface ILoadOrderCaches
{
    /// <summary>The facts of each base object.</summary>
    IBaseFacts Bases { get; }

    /// <summary>The archive index and the bounds of each mesh (<see cref="IMeshFiles.Bounds"/>).</summary>
    IMeshFiles MeshFiles { get; }

    /// <summary>The shape, size and kind of each base object.</summary>
    IBaseObjectShapes Shapes { get; }

    /// <summary>The triangle tree of each mesh.</summary>
    ITriangleMeshes Triangles { get; }

    /// <summary>The sampled surface of each mesh, sampled from <see cref="Triangles"/>.</summary>
    ISurfaceSamples SurfaceSamples { get; }

    /// <summary>The bounds of each set of body meshes.</summary>
    IBodyMeshBounds BodyBounds { get; }

    /// <summary>The bodies of NPCs.</summary>
    INpcBodies Bodies { get; }

    /// <summary>The terrain heights of each exterior cell.</summary>
    ITerrainHeights Terrain { get; }

    /// <summary>The navmesh triangles of each navmesh bucket.</summary>
    INavmeshes Navmeshes { get; }

    /// <summary>How the bounds of the base objects asked for so far were found, counted from <see cref="Bases"/>, <see cref="MeshFiles"/> and <see cref="Shapes"/>.</summary>
    BoundsStats CountBounds();

    /// <summary>Meshes of fog, light rays and the like among the meshes read so far.</summary>
    int CountEffectOnlyMeshes();

    /// <summary>Distinct sets of body meshes measured so far.</summary>
    int CountBodyMeshSets();
}
