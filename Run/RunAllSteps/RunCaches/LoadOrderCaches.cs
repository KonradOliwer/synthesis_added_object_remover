using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The load-order caches (C2 to C8, C13 and C14), created at the start of the run: the first creation point of
/// the run's caches. It also holds the problems the mesh reads meet and the statistics counted from the contents.
/// </summary>
internal sealed class LoadOrderCaches : ILoadOrderCaches
{
    private readonly PlacedNpcRules _placedNpcRules;

    private LoadOrderCaches(
        IBaseFacts bases,
        IMeshFiles meshFiles,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
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
        BodyBounds = bodyBounds;
        Bodies = bodies;
        Terrain = terrain;
        Navmeshes = navmeshes;
    }

    public IBaseFacts Bases { get; }

    public IMeshFiles MeshFiles { get; }

    public IBaseObjectShapes Shapes { get; }

    public ITriangleMeshes Triangles { get; }

    public IBodyMeshBounds BodyBounds { get; }

    public INpcBodies Bodies { get; }

    public ITerrainHeights Terrain { get; }

    public INavmeshes Navmeshes { get; }

    /// <remarks>The terrain and navmesh caches read through <paramref name="plugin"/>, so they answer once its placed records are read.</remarks>
    /// <param name="unexpectedErrors">The run's unexpected-error channel, which the mesh files record into.</param>
    /// <param name="npcBodyRules">How C8 works out the bodies of the NPCs.</param>
    /// <param name="placedNpcRules">How C10 picks, places and counts the placed NPCs.</param>
    public static LoadOrderCaches Create(
        IMeshFilesFactory meshFilesFactory,
        IUnexpectedErrors unexpectedErrors,
        IPluginRecords plugin,
        IBaseFacts bases,
        NpcBodyRules npcBodyRules,
        PlacedNpcRules placedNpcRules)
    {
        var meshFiles = meshFilesFactory.Open(BaseObjectShapes.SolidShapes, unexpectedErrors);
        var bodyBounds = new BodyMeshBounds(meshFiles);
        return new LoadOrderCaches(
            bases,
            meshFiles,
            new BaseObjectShapes(bases, meshFiles),
            new TriangleStore(meshFiles.ReadTriangles),
            bodyBounds,
            new NpcBodyCache(plugin, npcBodyRules.TemplateFlag, npcBodyRules.CreateOwnBodyResolver(meshFiles, bodyBounds), npcBodyRules.DistinctBodies),
            new TerrainHeights(plugin.ReadTerrain, plugin.FindLandWorldspace),
            new NavmeshIndex(plugin.ReadNavmeshes),
            placedNpcRules);
    }

    /// <summary>Creates C9 to C12 over the collected placed objects, together with these caches, as a new object; these caches stay as they are. The second creation point of the run.</summary>
    /// <param name="measurementRules">How C9 and C11 measure an object.</param>
    public CachesOfTheRun AddObjectCaches(CollectedObjects world, ObjectMeasurementRules measurementRules, Execution execution) =>
        new(this, ObjectCaches.Create(world, Shapes, Triangles, Bodies, measurementRules, _placedNpcRules, execution));

    public BoundsStats CountBounds() =>
        BaseObjectShapes.CountBounds(Shapes.Computed(), MeshFiles.Bounds.Computed(), MeshFiles.ArchivesIndexed);

    public int CountEffectOnlyMeshes() => MeshFiles.Bounds.Computed().Count(mesh => mesh.Status == MeshReadStatus.EffectOnly);

    public int CountBodyMeshSets() => BodyBounds.Computed().Count;

    /// <summary>Reads the archive index and warms up the mesh loader now, so their problems are met before any mesh is read.</summary>
    public void PrepareMeshReading() => MeshFiles.PrepareReading();
}
