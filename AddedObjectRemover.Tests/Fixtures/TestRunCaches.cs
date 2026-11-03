using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The run's caches a step and its rules read in a test; asking for a cache that was not given fails the test.</summary>
/// <param name="objectsOfAnyPlugin">Gives the visible objects of any plugin; called on every ask.</param>
/// <param name="surfaces">The surface samples; samples over <paramref name="triangles"/> when null.</param>
/// <param name="visibleTargets">Answers <see cref="IndexVisibleTargets"/>.</param>
/// <param name="objectsOfAnyPluginAsTheyStand">Answers <see cref="VisibleObjectsOfAnyPluginAsTheyStand"/>.</param>
/// <param name="objects">Answers every object cache that no other parameter answers.</param>
/// <param name="shownTargets">Answers <see cref="IndexShownTargets"/>.</param>
internal sealed class TestRunCaches(
    IBaseObjectShapes shapes,
    ITriangleMeshes? triangles = null,
    ITerrainHeights? terrain = null,
    IBaseFacts? bases = null,
    Func<IVisibleObjectsOfAnyPlugin>? objectsOfAnyPlugin = null,
    ISurfaceSamples? surfaces = null,
    INavmeshes? navmeshes = null,
    Func<IReadOnlyList<TargetObject>, IVisibleTargetObjects>? visibleTargets = null,
    Func<IReadOnlyDictionary<OtherId, OtherObject?>, IVisibleObjectsOfAnyPlugin>? objectsOfAnyPluginAsTheyStand = null,
    IObjectCaches? objects = null,
    Func<IReadOnlyList<TargetObject>, IVisibleTargetObjects>? shownTargets = null) : IRunCaches
{
    private readonly ISurfaceSamples? _surfaces = surfaces ?? (triangles is null ? null : TestSurfaceSamples.Over(triangles));

    public IBaseFacts Bases => bases ?? throw NotGiven();

    public IBaseObjectShapes Shapes => shapes;

    public ITriangleMeshes Triangles => triangles ?? throw NotGiven();

    public ISurfaceSamples SurfaceSamples => _surfaces ?? throw NotGiven();

    public ITerrainHeights Terrain => terrain ?? throw NotGiven();

    public IMeshFiles MeshFiles => throw NotGiven();

    public IBodyMeshBounds BodyBounds => throw NotGiven();

    public INpcBodies Bodies => throw NotGiven();

    public INavmeshes Navmeshes => navmeshes ?? throw NotGiven();

    public IOtherModObjectsBySpace OtherModObjects => Objects.OtherModObjects;

    public IPlacedNpcsBySpace PlacedNpcs => Objects.PlacedNpcs;

    public IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin => Objects.ObjectsOfAnyPlugin;

    public IVisibleTargetObjects VisibleTargets => Objects.VisibleTargets;

    public IVisibleTargetObjects IndexVisibleTargets(IReadOnlyList<TargetObject> targets) =>
        visibleTargets is null ? Objects.IndexVisibleTargets(targets) : visibleTargets(targets);

    public IVisibleTargetObjects IndexShownTargets(IReadOnlyList<TargetObject> targets) =>
        shownTargets is null ? Objects.IndexShownTargets(targets) : shownTargets(targets);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => objectsOfAnyPlugin is null ? Objects.VisibleObjectsOfAnyPlugin() : objectsOfAnyPlugin();

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPluginAsTheyStand(IReadOnlyDictionary<OtherId, OtherObject?> changed) =>
        objectsOfAnyPluginAsTheyStand is null ? Objects.VisibleObjectsOfAnyPluginAsTheyStand(changed) : objectsOfAnyPluginAsTheyStand(changed);

    public BoundsStats CountBounds() => throw NotGiven();

    public int CountEffectOnlyMeshes() => throw NotGiven();

    public int CountBodyMeshSets() => throw NotGiven();

    private IObjectCaches Objects => objects ?? throw NotGiven();

    private static InvalidOperationException NotGiven() => new("The test gave the caches no answer to this.");
}
