using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The run's caches the also-remove step reads; asking for any other cache fails the test.</summary>
/// <param name="objectsOfAnyPlugin">Gives the visible objects of any plugin; called on every ask.</param>
internal sealed class TestRunCaches(
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
    ITerrainHeights terrain,
    IBaseFacts bases,
    Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin) : IRunCaches
{
    public IBaseFacts Bases => bases;

    public IBaseObjectShapes Shapes => shapes;

    public ITriangleMeshes Triangles => triangles;

    public ITerrainHeights Terrain => terrain;

    public IMeshFiles MeshFiles => throw NotRead();

    public IBodyMeshBounds BodyBounds => throw NotRead();

    public INpcBodies Bodies => throw NotRead();

    public INavmeshes Navmeshes => throw NotRead();

    public IOtherModObjectsBySpace OtherModObjects => throw NotRead();

    public IPlacedNpcsBySpace PlacedNpcs => throw NotRead();

    public IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin => throw NotRead();

    public IVisibleTargetObjects VisibleTargets => throw NotRead();

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => objectsOfAnyPlugin();

    public BoundsStats CountBounds() => throw NotRead();

    public int CountEffectOnlyMeshes() => throw NotRead();

    public int CountBodyMeshSets() => throw NotRead();

    private static InvalidOperationException NotRead() => new("The also-remove step does not read this cache.");
}
