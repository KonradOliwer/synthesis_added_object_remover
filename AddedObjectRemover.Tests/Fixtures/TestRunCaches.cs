using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The run's caches a step and its rules read in a test; asking for a cache that was not given fails the test.</summary>
/// <param name="objectsOfAnyPlugin">Gives the visible objects of any plugin; called on every ask.</param>
/// <param name="surfaces">The surface samples; samples over <paramref name="triangles"/> when null.</param>
/// <param name="visibleTargetsAsTheRoundsMeasureThem">Answers <see cref="IndexVisibleTargetsAsTheRoundsMeasureThem"/>.</param>
/// <param name="visibleTargetsAsTheyStand">Answers <see cref="IndexVisibleTargetsAsTheyStand"/>.</param>
/// <param name="objectsOfAnyPluginAsTheyStand">Answers <see cref="VisibleObjectsOfAnyPluginAsTheyStand"/>.</param>
internal sealed class TestRunCaches(
    IBaseObjectShapes shapes,
    ITriangleMeshes? triangles = null,
    ITerrainHeights? terrain = null,
    IBaseFacts? bases = null,
    Func<IVisibleObjectsOfAnyPlugin>? objectsOfAnyPlugin = null,
    ISurfaceSamples? surfaces = null,
    INavmeshes? navmeshes = null,
    Func<IRemovalDecisions, IVisibleTargetObjects>? visibleTargetsAsTheRoundsMeasureThem = null,
    Func<IRemovalDecisions, IVisibleTargetObjects>? visibleTargetsAsTheyStand = null,
    Func<IReadOnlyDictionary<OtherId, OtherObject?>, IVisibleObjectsOfAnyPlugin>? objectsOfAnyPluginAsTheyStand = null) : IRunCaches
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

    public IOtherModObjectsBySpace OtherModObjects => throw NotGiven();

    public IPlacedNpcsBySpace PlacedNpcs => throw NotGiven();

    public IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin => throw NotGiven();

    public IVisibleTargetObjects VisibleTargets => throw NotGiven();

    public IVisibleTargetObjects IndexVisibleTargets(IReadOnlyList<TargetObject> targets) => throw NotGiven();

    public IVisibleTargetObjects IndexVisibleTargetsAsTheRoundsMeasureThem(IRemovalDecisions decisions) =>
        (visibleTargetsAsTheRoundsMeasureThem ?? throw NotGiven())(decisions);

    public IVisibleTargetObjects IndexVisibleTargetsAsTheyStand(IRemovalDecisions decisions) => (visibleTargetsAsTheyStand ?? throw NotGiven())(decisions);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => (objectsOfAnyPlugin ?? throw NotGiven())();

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPluginAsTheyStand(IReadOnlyDictionary<OtherId, OtherObject?> changed) =>
        (objectsOfAnyPluginAsTheyStand ?? throw NotGiven())(changed);

    public BoundsStats CountBounds() => throw NotGiven();

    public int CountEffectOnlyMeshes() => throw NotGiven();

    public int CountBodyMeshSets() => throw NotGiven();

    private static InvalidOperationException NotGiven() => new("The test gave the caches no answer to this.");
}
