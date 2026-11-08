using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches;

/// <summary>
/// The load-order caches, which need no placed objects. It also holds the problems the mesh reads meet and the statistics
/// counted from the contents.
/// </summary>
public sealed class LoadOrderCaches : ILoadOrderCaches
{
    private readonly PlacedNpcRules _placedNpcRules;

    private LoadOrderCaches(
        IBaseFacts bases,
        IMeshFiles meshFiles,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        ISurfaceSamples surfaceSamples,
        IBodyMeshBounds bodyBounds,
        INpcBodies bodies,
        ITerrainHeights terrain,
        INavmeshes navmeshes,
        PlacedNpcRules placedNpcRules)
    {
        _placedNpcRules = placedNpcRules;
        Bases = bases;
        MeshFiles = meshFiles;
        Shapes = shapes;
        Triangles = triangles;
        SurfaceSamples = surfaceSamples;
        BodyBounds = bodyBounds;
        Bodies = bodies;
        Terrain = terrain;
        Navmeshes = navmeshes;
    }

    public IBaseFacts Bases { get; }

    public IMeshFiles MeshFiles { get; }

    public IBaseObjectShapes Shapes { get; }

    public ITriangleMeshes Triangles { get; }

    public ISurfaceSamples SurfaceSamples { get; }

    public IBodyMeshBounds BodyBounds { get; }

    public INpcBodies Bodies { get; }

    public ITerrainHeights Terrain { get; }

    public INavmeshes Navmeshes { get; }

    /// <remarks>The terrain and navmesh caches read through <paramref name="plugin"/>, so they answer once its placed records are read.</remarks>
    /// <param name="shapes">The shapes of the base objects, measured from <paramref name="meshFiles"/>.</param>
    /// <param name="npcBodyRules">How the resolved NPC bodies are worked out for the NPCs.</param>
    /// <param name="placedNpcRules">How the spawning NPCs of a space pick, places and counts the placed NPCs.</param>
    /// <param name="surfaceSamplesPerMesh">How many points the surface sample spreads over each mesh's surface.</param>
    public static LoadOrderCaches Create(
        IMeshFiles meshFiles,
        IBaseObjectShapes shapes,
        IPluginRecords plugin,
        IBaseFacts bases,
        NpcBodyRules npcBodyRules,
        PlacedNpcRules placedNpcRules,
        int surfaceSamplesPerMesh)
    {
        var bodyBounds = new BodyMeshBounds(meshFiles);
        var triangles = new ResidentMeshTrees(meshFiles.ReadTriangles);
        return new LoadOrderCaches(
            bases,
            meshFiles,
            shapes,
            triangles,
            new SampledMeshSurfaces(triangles, surfaceSamplesPerMesh),
            bodyBounds,
            new ResolvedNpcBodies(plugin, npcBodyRules.TemplateFlag, npcBodyRules.CreateOwnBodyResolver(meshFiles, bodyBounds), npcBodyRules.DistinctBodies),
            new TerrainHeights(plugin.ReadTerrain, plugin.FindLandWorldspace),
            new NavmeshIndex(plugin.ReadNavmeshes),
            placedNpcRules);
    }

    /// <summary>Creates the object caches over the collected placed objects, together with these caches, as a new object; these caches stay as they are.</summary>
    /// <param name="measurementRules">How the other-mod and any-plugin object caches measure an object.</param>
    public IRunCaches AddObjectCaches(CollectedObjects world, ObjectMeasurementRules measurementRules, Execution execution) =>
        new CachesOfTheRun(this, ObjectCaches.Create(world, Shapes, Triangles, Bodies, measurementRules, _placedNpcRules, execution));

    /// <summary>Creates the object caches over worlds other than the run's, each added to these caches on its first ask.</summary>
    /// <param name="measurementRules">How the object caches of each world measure an object.</param>
    public IObjectCachesOfWorlds CreateWorldCaches(ObjectMeasurementRules measurementRules, Execution execution) =>
        new ObjectCachesOfWorlds(this, measurementRules, execution);

    public BoundsStats CountBounds() => Shapes.CountBounds();

    public int CountEffectOnlyMeshes() => MeshFiles.Bounds.Computed().Count(mesh => mesh.Status == MeshReadStatus.EffectOnly);

    public int CountBodyMeshSets() => BodyBounds.Computed().Count;
}
