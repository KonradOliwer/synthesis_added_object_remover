using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>The caches of one run once the placed objects are collected: the load-order caches it was given and the object caches (C9 to C12). Neither changes after this is created.</summary>
internal sealed class CachesOfTheRun(ILoadOrderCaches loadOrder, ObjectCaches objects) : IRunCaches
{
    public IBaseFacts Bases => loadOrder.Bases;

    public IMeshFiles MeshFiles => loadOrder.MeshFiles;

    public IBaseObjectShapes Shapes => loadOrder.Shapes;

    public ITriangleMeshes Triangles => loadOrder.Triangles;

    public IBodyMeshBounds BodyBounds => loadOrder.BodyBounds;

    public INpcBodies Bodies => loadOrder.Bodies;

    public ITerrainHeights Terrain => loadOrder.Terrain;

    public INavmeshes Navmeshes => loadOrder.Navmeshes;

    public BoundsStats CountBounds() => loadOrder.CountBounds();

    public int CountEffectOnlyMeshes() => loadOrder.CountEffectOnlyMeshes();

    public int CountBodyMeshSets() => loadOrder.CountBodyMeshSets();

    public IOtherModObjectsBySpace OtherModObjects => objects.OtherModObjects;

    public IPlacedNpcsBySpace PlacedNpcs => objects.PlacedNpcs;

    public IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin => objects.ObjectsOfAnyPlugin;

    public IVisibleTargetObjects VisibleTargets => objects.VisibleTargets;

    public IVisibleTargetObjects IndexVisibleTargets(IReadOnlyList<TargetObject> targets) => objects.IndexVisibleTargets(targets);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => objects.VisibleObjectsOfAnyPlugin();

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPluginAsTheyStand(IReadOnlyDictionary<OtherId, OtherObject?> changed) =>
        objects.VisibleObjectsOfAnyPluginAsTheyStand(changed);
}
