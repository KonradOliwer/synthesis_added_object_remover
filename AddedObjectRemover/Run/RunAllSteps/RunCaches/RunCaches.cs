using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The caches of one run: the load-order caches (C2 to C8, C13 and C14) created at the start of the run, and the
/// object caches (C9 to C12) created once the placed objects are collected, with the problems the mesh reads
/// meet and the statistics counted from their contents.
/// </summary>
internal sealed class RunCaches : IRunCaches
{
    private readonly ProblemCapture _capture;
    private ObjectCaches? _objects;

    private RunCaches(
        IBaseFacts bases,
        IMeshFiles meshFiles,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        IBodyMeshBounds bodyBounds,
        INpcBodies bodies,
        ITerrainHeights terrain,
        INavmeshes navmeshes)
    {
        Bases = bases;
        MeshFiles = meshFiles;
        Shapes = shapes;
        Triangles = triangles;
        BodyBounds = bodyBounds;
        Bodies = bodies;
        Terrain = terrain;
        Navmeshes = navmeshes;
        _capture = new ProblemCapture(meshFiles.Problems);
    }

    public IBaseFacts Bases { get; }

    public IMeshFiles MeshFiles { get; }

    public IBaseObjectShapes Shapes { get; }

    public ITriangleMeshes Triangles { get; }

    public IBodyMeshBounds BodyBounds { get; }

    public INpcBodies Bodies { get; }

    public ITerrainHeights Terrain { get; }

    public INavmeshes Navmeshes { get; }

    public IOtherModObjectsBySpace OtherModObjects => Objects.OtherModObjects;

    public IPlacedNpcsBySpace PlacedNpcs => Objects.PlacedNpcs;

    public IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin => Objects.ObjectsOfAnyPlugin;

    public IVisibleTargetObjects VisibleTargets => Objects.VisibleTargets;

    public IOtherModObjectPositions OtherModObjectPositions() => Objects.OtherModObjectPositions();

    public IObjectsThatCanCauseRemovals ObjectsThatCanCauseRemovals(Replacements replaced, NpcHandling npcs) =>
        Objects.ObjectsThatCanCauseRemovals(replaced, npcs);

    public INpcsThatCanSpawn NpcsThatCanSpawn(Replacements replaced) => Objects.NpcsThatCanSpawn(replaced);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => Objects.VisibleObjectsOfAnyPlugin();

    private ObjectCaches Objects =>
        _objects ?? throw new InvalidOperationException("The object caches are asked for before the placed objects are indexed.");

    /// <summary>Creates C9 to C12 over the collected placed objects; the second creation point of the run.</summary>
    public RunCaches IndexObjects(CollectedObjects world, Execution execution)
    {
        _objects = ObjectCaches.Create(world, Shapes, Triangles, Bodies, RecordUnexpected, execution);
        return this;
    }

    /// <remarks>The terrain and navmesh caches read through <paramref name="plugin"/>, so they answer once its placed records are read.</remarks>
    public static RunCaches CreateForLoadOrder(IMeshFilesFactory meshFilesFactory, IPluginRecords plugin, IBaseFacts bases)
    {
        var meshFiles = meshFilesFactory.Open(BaseObjectRules.SolidShapes);
        var bodyBounds = new BodyMeshBounds(meshFiles);
        return new RunCaches(
            bases,
            meshFiles,
            new BaseObjectShapes(bases, meshFiles),
            new TriangleStore(meshFiles.ReadTriangles),
            bodyBounds,
            new NpcBodyCache(plugin, NpcTemplateFlag.Traits, new NpcBodyResolver(meshFiles, bodyBounds)),
            new TerrainHeights(plugin.ReadTerrain, plugin.FindLandWorldspace),
            new NavmeshIndex(plugin.ReadNavmeshes));
    }

    public BoundsStats CountBounds() =>
        BaseObjectShapes.CountBounds(Shapes.Computed(), MeshFiles.Bounds.Computed(), MeshFiles.ArchivesIndexed);

    public int CountEffectOnlyMeshes() => MeshFiles.Bounds.Computed().Count(mesh => mesh.Status == MeshReadStatus.EffectOnly);

    public int CountBodyMeshSets() => BodyBounds.Computed().Count;

    /// <summary>Reads the archive index and warms up the mesh loader now, so their problems are met before any mesh is read.</summary>
    public void PrepareMeshReading() => MeshFiles.PrepareReading();

    /// <param name="detailed">Whether the detailed log is on.</param>
    public ReportContext ReportContext(bool detailed) => new(Bases, Shapes, detailed);

    /// <summary>The mesh problems met since the last call; call it right after each phase that reads meshes.</summary>
    public PhaseProblems TakeProblems() => _capture.Take();

    /// <summary>The archive and data-folder problems met since the last call.</summary>
    public ImmutableArray<ArchiveProblem> TakeArchiveProblems() => _capture.TakeArchive();

    /// <summary>Records an error caught where one part of the run is processed; thread-safe.</summary>
    public void RecordUnexpected(UnexpectedError error) => MeshFiles.Problems.Add(error);

    /// <summary>The unexpected errors recorded since the last call.</summary>
    public ImmutableArray<UnexpectedError> TakeUnexpectedErrors() => _capture.TakeUnexpected();

    /// <summary>Every mesh problem of the run so far, whether taken or not.</summary>
    public PhaseProblems AllProblems() => new(MeshFiles.Problems.Since(default));
}
