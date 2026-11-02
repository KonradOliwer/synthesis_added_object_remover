using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The load-order caches, which need no placed objects. It also holds the problems the mesh reads meet and the statistics
/// counted from the contents.
/// </summary>
internal sealed class LoadOrderCaches : ILoadOrderCaches
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
    /// <param name="collisionLayers">The load order's collision layer records, by layer number.</param>
    /// <param name="npcBodyRules">How the NPC body cache works out the bodies of the NPCs.</param>
    /// <param name="placedNpcRules">How the placed-NPC index picks, places and counts the placed NPCs.</param>
    /// <param name="surfaceSamplesPerMesh">How many points the surface sample spreads over each mesh's surface.</param>
    public static LoadOrderCaches Create(
        IMeshFilesFactory meshFilesFactory,
        IPluginRecords plugin,
        IBaseFacts bases,
        IReadOnlyDictionary<uint, CollisionLayerFact> collisionLayers,
        NpcBodyRules npcBodyRules,
        PlacedNpcRules placedNpcRules,
        int surfaceSamplesPerMesh)
    {
        var meshFiles = meshFilesFactory.Open(BaseObjectShapes.SolidShapes);
        var bodyBounds = new BodyMeshBounds(meshFiles);
        var triangles = new TriangleStore(meshFiles.ReadTriangles);
        return new LoadOrderCaches(
            bases,
            meshFiles,
            new BaseObjectShapes(bases, meshFiles, collisionLayers),
            triangles,
            new SurfaceSampleStore(triangles, surfaceSamplesPerMesh),
            bodyBounds,
            new NpcBodyCache(plugin, npcBodyRules.TemplateFlag, npcBodyRules.CreateOwnBodyResolver(meshFiles, bodyBounds), npcBodyRules.DistinctBodies),
            new TerrainHeights(plugin.ReadTerrain, plugin.FindLandWorldspace),
            new NavmeshIndex(plugin.ReadNavmeshes),
            placedNpcRules);
    }

    /// <summary>Creates the object caches over the collected placed objects, together with these caches, as a new object; these caches stay as they are.</summary>
    /// <param name="measurementRules">How the other-mod and any-plugin object caches measure an object.</param>
    public CachesOfTheRun AddObjectCaches(CollectedObjects world, ObjectMeasurementRules measurementRules, Execution execution) =>
        new(this, ObjectCaches.Create(world, Shapes, Triangles, Bodies, measurementRules, _placedNpcRules, execution));

    public BoundsStats CountBounds() =>
        BaseObjectShapes.CountBounds(Shapes.Computed(), MeshFiles.Bounds.Computed(), MeshFiles.ArchivesIndexed);

    public int CountEffectOnlyMeshes() => MeshFiles.Bounds.Computed().Count(mesh => mesh.Status == MeshReadStatus.EffectOnly);

    public int CountBodyMeshSets() => BodyBounds.Computed().Count;

    /// <summary>Reads the archive index and warms up the mesh loader now, so their problems are met before any mesh is read.</summary>
    public void PrepareMeshReading() => MeshFiles.PrepareReading();
}
